using System;
using System.Collections.Generic;
using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;
using GrandStrategy.Systems.Diplomacy;
using GrandStrategy.Systems.Economy;
using GrandStrategy.SimCore;

namespace GrandStrategy.Systems.Trade;

/// <summary>
/// Менеджер торговли (Autoload #10). Внутренняя торговля подразумевается единым
/// национальным рынком (агрегируется в EconomyManager). Здесь — внешняя (межстрановая)
/// торговля по гравитационной модели: поток = избыток × дефицит, модифицированный
/// тарифами, санкциями, эмбарго, войной, расстоянием и отношениями.
/// </summary>
public partial class TradeManager : Node
{
    public static TradeManager Instance { get; private set; } = null!;

    public override void _Ready() => Instance = this;

    public void Tick(WorldData world, WorldEconomy economy)
    {
        // Обнуление торговых балансов и импорта/экспорта.
        for (int c = 0; c < world.CountryCount; c++)
        {
            economy.Countries[c].TradeBalance = 0.0;
            Array.Clear(economy.Countries[c].Consumption, 0, economy.Countries[c].Consumption.Length);
        }

        for (int g = 0; g < world.GoodCount; g++)
        {
            if (!world.Goods[g].Tradeable)
                continue;
            TradeGood(world, economy, g);
        }
    }

    private static void TradeGood(WorldData world, WorldEconomy economy, int goodId)
    {
        int n = world.CountryCount;
        var exporters = new List<int>();
        var importers = new List<int>();
        var surplus = new double[n];
        var deficit = new double[n];

        for (int c = 0; c < n; c++)
        {
            CountryEconomy eco = economy.Countries[c];
            surplus[c] = Math.Max(eco.Supply[goodId] - eco.Demand[goodId], 0.0);
            deficit[c] = Math.Max(eco.Demand[goodId] - eco.Supply[goodId], 0.0);
            if (surplus[c] > 1.0) exporters.Add(c);
            if (deficit[c] > 1.0) importers.Add(c);
        }

        if (exporters.Count == 0 || importers.Count == 0)
            return;

        // Сортируем: экспортёры по убыванию избытка, импортёры по убыванию дефицита.
        exporters.Sort((a, b) => surplus[b].CompareTo(surplus[a]));
        importers.Sort((a, b) => deficit[b].CompareTo(deficit[a]));

        foreach (int b in importers)
        {
            foreach (int a in exporters)
            {
                if (a == b)
                    continue;
                if (deficit[b] <= 1.0 || surplus[a] <= 1.0)
                    continue;

                if (!CanTrade(world, a, b))
                    continue;

                double eff = TradeEfficiency(world, a, b);
                if (eff <= 0.0)
                    continue;

                double flow = Math.Min(surplus[a], deficit[b]) * eff;
                if (flow <= 0.0)
                    continue;

                double price = economy.Countries[a].Price[goodId];

                // Перемещение товара и денег.
                economy.Countries[a].Supply[goodId] -= flow;
                economy.Countries[b].Supply[goodId] += flow;
                surplus[a] -= flow;
                deficit[b] -= flow;

                double value = flow * price;
                // Масштабируем торговую стоимость к ВВП: реальный экспорт/импорт —
                // заметная доля экономики (игровые единицы несопоставимы с $трлн ВВП).
                double scale = economy.Countries[a].BaselineGdp > 0
                    ? economy.Countries[a].BaselineGdp / 1_000_000.0 // ~0.1% ВВП на единицу
                    : 1.0;
                value *= Math.Max(scale, 1.0);

                economy.Countries[a].TradeBalance += value;                       // экспорт
                economy.Countries[b].TradeBalance -= value;                       // импорт
                economy.Countries[b].Consumption[goodId] += flow;

                // Тариф импортёра зависит от категории товара: стратегические/энергия дороже.
                double tariff = economy.Countries[b].Taxes.ImportTariff;
                GoodCategory cat = world.Goods[goodId].Category;
                if (cat is GoodCategory.Strategic or GoodCategory.Energy)
                    tariff *= 1.5; // повышенные пошлины на стратегические товары
                double exportTax = economy.Countries[a].Taxes.ExportTariff;
                economy.Countries[b].BudgetRevenue += value * tariff;
                economy.Countries[a].BudgetRevenue += value * exportTax;

                // Торговля мягко улучшает отношения (с причиной).
                DiplomacyManager.Instance.ChangeRelation(a, b, 0.01f, "REL_TRADE");
                DiplomacyManager.Instance.ChangeRelation(b, a, 0.01f, "REL_TRADE");
            }
        }
    }

    /// <summary>Есть ли у страны порт (прибрежная провинция с портом или любая прибрежная).</summary>
    private static bool HasPort(WorldData world, int countryId)
    {
        CountryData c = world.GetCountry(countryId);
        if (c == null)
            return false;
        foreach (int pid in c.OwnedProvinceIds)
        {
            ProvinceData p = world.GetProvince(pid);
            if (p.IsCoastal)
                return true;
        }
        return false;
    }

    private static bool CanTrade(WorldData world, int a, int b)
    {
        CountryData ca = world.Countries[a];
        CountryData cb = world.Countries[b];

        if (DiplomacyManager.Instance.AreAtWar(a, b))
            return false;
        if (ca.TradePolicy.Embargoed.Contains(b) || cb.TradePolicy.Embargoed.Contains(a))
            return false;
        if (ca.TradePolicy.Sanctioned.Contains(b))
            return false;
        return true;
    }

    private static double TradeEfficiency(WorldData world, int a, int b)
    {
        CountryData ca = world.Countries[a];
        CountryData cb = world.Countries[b];

        double relations = (ca.RelationWith(b) + 100.0) / 200.0; // 0..1
        int agreement = cb.TradePolicy.Agreements.TryGetValue(a, out TradeAgreement agree)
            ? (int)agree : 0;
        bool sanctioned = ca.TradePolicy.Sanctioned.Contains(b) || cb.TradePolicy.Sanctioned.Contains(a);

        double dist = 0.0;
        int caPro = ca.CapitalProvinceId;
        int cbPro = cb.CapitalProvinceId;
        if (caPro >= 0 && cbPro >= 0)
        {
            Vector2 pa = world.GetProvince(caPro).Centroid;
            Vector2 pb = world.GetProvince(cbPro).Centroid;
            dist = pa.DistanceTo(pb);
        }

        // Морская торговля: если обе страны имеют порт — расстояние дешевле (море).
        if (HasPort(world, a) && HasPort(world, b))
            dist *= 0.6;

        return SimFormulas.TradeEfficiency(relations, agreement, sanctioned, dist);
    }
}
