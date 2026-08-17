using System.Collections.Generic;
using GrandStrategy.Systems.Economy;

namespace GrandStrategy.Data;

// --- DTO сохранения (сериализуемые, без Vector2 и ссылочных Godot-типов) ----------

public sealed class WorldStateSaveDto
{
    public int ProvinceCount { get; set; }
    public int CountryCount { get; set; }
    public List<ProvinceSaveDto> Provinces { get; set; } = new();
    public List<CountrySaveDto> Countries { get; set; } = new();
    public List<ArmySaveDto> Armies { get; set; } = new();
    public List<WarSaveDto> Wars { get; set; } = new();
    public List<EconomySaveDto> Economies { get; set; } = new();
}

public sealed class ProvinceSaveDto
{
    public int Id;
    public int OwnerId;
    public int ControllerId;
    public long MaleChildren, FemaleChildren, MaleTeens, FemaleTeens;
    public long MaleAdults, FemaleAdults, MaleSeniors, FemaleSeniors;
    public float Infrastructure, Development, TaxBase, Unrest, Autonomy;
    public List<int> BuildingIds = new();
}

public sealed class CountrySaveDto
{
    public int Id;
    public string Code = string.Empty;
    public string NameKey = string.Empty;
    public int GovernmentType;
    public int Ideology;
    public float Stability, Legitimacy, WarExhaustion;
    public double Treasury, Debt, Inflation, BaseInterestRate, Gdp;
    public long Population;
    public float Urbanization, Literacy, TechLevel;
    public int CreditRating;
    public int CurrencyId;
    public List<int> Laws = new();
    public Dictionary<int, float> Relations = new();
    public int CapitalProvinceId;
    public List<int> OwnedProvinceIds = new();
    public List<int> ControlledProvinceIds = new();
    public int AiProfile;
    public bool IsPlayer, IsAlive;
    public string ColorHex = "#888888";
    public string FlagId = string.Empty;
}

public sealed class ArmySaveDto
{
    public int Id, OwnerId, ProvinceId;
    public Dictionary<int, int> UnitCounts = new();
    public float Strength, Morale, Organization;
    public double Supply;
    public int CommanderId, FortLevel;
    public List<int> MoveOrder = new();
}

public sealed class WarSaveDto
{
    public int Id, AttackerId, DefenderId;
    public List<int> AllyIds = new();
    public string WarGoals = string.Empty;
    public int StartTurn, Battles;
    public List<int> OccupiedProvinces = new();
    public double WarScore;
}

public sealed class EconomySaveDto
{
    public double LaborForce, Employment, UnemploymentRate;
    public double Gdp, GdpPerCapita, AvgWage;
    public double BudgetRevenue, BudgetExpenses, Deficit, TradeBalance;
    public double Inflation, InterestRate, Debt, DebtToGdp, Reserves;
    public double[] Supply = System.Array.Empty<double>();
    public double[] Demand = System.Array.Empty<double>();
    public double[] Price = System.Array.Empty<double>();
    public double[] Production = System.Array.Empty<double>();
    public double[] Consumption = System.Array.Empty<double>();
    // Налоги (управляются игроком).
    public double TaxIncome, TaxCorporate, TaxVat, TaxProperty, TaxResource, TaxImport, TaxExport, TaxLuxury;
}

/// <summary>
/// Кодировщик мирового состояния в DTO и обратно (для SaveManager).
/// Статические данные (соседство, ядра, ресурсы, центроиды) восстанавливаются из
/// world.json, поэтому не сохраняются — сохраняем только динамическое состояние.
/// </summary>
public static class WorldSaveCodec
{
    public static WorldStateSaveDto Encode(WorldData world, WorldEconomy economy,
        List<ArmyData> armies, List<WarData> wars)
    {
        var dto = new WorldStateSaveDto
        {
            ProvinceCount = world.ProvinceCount,
            CountryCount = world.CountryCount,
        };

        for (int i = 0; i < world.ProvinceCount; i++)
        {
            ProvinceData p = world.Provinces[i];
            dto.Provinces.Add(new ProvinceSaveDto
            {
                Id = p.Id,
                OwnerId = p.OwnerId,
                ControllerId = p.ControllerId,
                MaleChildren = p.MaleChildren, FemaleChildren = p.FemaleChildren,
                MaleTeens = p.MaleTeens, FemaleTeens = p.FemaleTeens,
                MaleAdults = p.MaleAdults, FemaleAdults = p.FemaleAdults,
                MaleSeniors = p.MaleSeniors, FemaleSeniors = p.FemaleSeniors,
                Infrastructure = p.Infrastructure, Development = p.Development,
                TaxBase = p.TaxBase, Unrest = p.Unrest, Autonomy = p.Autonomy,
                BuildingIds = new List<int>(p.BuildingIds),
            });
        }

        for (int i = 0; i < world.CountryCount; i++)
        {
            CountryData c = world.Countries[i];
            if (c == null)
                continue;
            dto.Countries.Add(new CountrySaveDto
            {
                Id = c.Id, Code = c.Code, NameKey = c.NameKey,
                GovernmentType = (int)c.GovernmentType, Ideology = (int)c.Ideology,
                Stability = c.Stability, Legitimacy = c.Legitimacy, WarExhaustion = c.WarExhaustion,
                Treasury = c.Treasury, Debt = c.Debt, Inflation = c.Inflation,
                BaseInterestRate = c.BaseInterestRate, Gdp = c.Gdp, Population = c.Population,
                Urbanization = c.Urbanization, Literacy = c.Literacy, TechLevel = c.TechLevel,
                CreditRating = (int)c.CreditRating, CurrencyId = c.CurrencyId,
                Laws = new List<int>(c.Laws), Relations = new Dictionary<int, float>(c.Relations),
                CapitalProvinceId = c.CapitalProvinceId,
                OwnedProvinceIds = new List<int>(c.OwnedProvinceIds),
                ControlledProvinceIds = new List<int>(c.ControlledProvinceIds),
                AiProfile = (int)c.AiProfile, IsPlayer = c.IsPlayer, IsAlive = c.IsAlive,
                ColorHex = c.Color.ToHtml(), FlagId = c.FlagId,
            });
        }

        foreach (ArmyData a in armies)
            dto.Armies.Add(new ArmySaveDto
            {
                Id = a.Id, OwnerId = a.OwnerId, ProvinceId = a.ProvinceId,
                UnitCounts = new Dictionary<int, int>(a.UnitCounts),
                Strength = a.Strength, Morale = a.Morale, Organization = a.Organization,
                Supply = a.Supply, CommanderId = a.CommanderId, FortLevel = a.FortLevel,
                MoveOrder = new List<int>(a.MoveOrder),
            });

        foreach (WarData w in wars)
            dto.Wars.Add(new WarSaveDto
            {
                Id = w.Id, AttackerId = w.AttackerId, DefenderId = w.DefenderId,
                AllyIds = new List<int>(w.AllyIds), WarGoals = w.WarGoals,
                StartTurn = w.StartTurn, Battles = w.Battles,
                OccupiedProvinces = new List<int>(w.OccupiedProvinces), WarScore = w.WarScore,
            });

        for (int i = 0; i < economy.Countries.Length; i++)
        {
            CountryEconomy eco = economy.Countries[i];
            dto.Economies.Add(new EconomySaveDto
            {
                LaborForce = eco.LaborForce, Employment = eco.Employment,
                UnemploymentRate = eco.UnemploymentRate, Gdp = eco.Gdp,
                GdpPerCapita = eco.GdpPerCapita, AvgWage = eco.AvgWage,
                BudgetRevenue = eco.BudgetRevenue, BudgetExpenses = eco.BudgetExpenses,
                Deficit = eco.Deficit, TradeBalance = eco.TradeBalance,
                Inflation = eco.Inflation, InterestRate = eco.InterestRate,
                Debt = eco.Debt, DebtToGdp = eco.DebtToGdp, Reserves = eco.Reserves,
                Supply = (double[])eco.Supply.Clone(),
                Demand = (double[])eco.Demand.Clone(),
                Price = (double[])eco.Price.Clone(),
                Production = (double[])eco.Production.Clone(),
                Consumption = (double[])eco.Consumption.Clone(),
                TaxIncome = eco.Taxes.Income, TaxCorporate = eco.Taxes.Corporate,
                TaxVat = eco.Taxes.Vat, TaxProperty = eco.Taxes.Property,
                TaxResource = eco.Taxes.Resource, TaxImport = eco.Taxes.ImportTariff,
                TaxExport = eco.Taxes.ExportTariff, TaxLuxury = eco.Taxes.Luxury,
            });
        }

        return dto;
    }

    public static void Apply(WorldData world, WorldEconomy economy, WorldStateSaveDto dto,
        List<ArmyData> armiesOut, List<WarData> warsOut)
    {
        // --- Провинции ---
        foreach (ProvinceSaveDto p in dto.Provinces)
        {
            if (!world.ProvinceIdToIndex.TryGetValue(p.Id, out int idx))
                continue;
            ProvinceData cur = world.Provinces[idx];
            cur.OwnerId = p.OwnerId;
            cur.ControllerId = p.ControllerId;
            cur.MaleChildren = (int)p.MaleChildren; cur.FemaleChildren = (int)p.FemaleChildren;
            cur.MaleTeens = (int)p.MaleTeens; cur.FemaleTeens = (int)p.FemaleTeens;
            cur.MaleAdults = (int)p.MaleAdults; cur.FemaleAdults = (int)p.FemaleAdults;
            cur.MaleSeniors = (int)p.MaleSeniors; cur.FemaleSeniors = (int)p.FemaleSeniors;
            cur.Infrastructure = p.Infrastructure; cur.Development = p.Development;
            cur.TaxBase = p.TaxBase; cur.Unrest = p.Unrest; cur.Autonomy = p.Autonomy;
            cur.BuildingIds = p.BuildingIds.ToArray();
            world.Provinces[idx] = cur;
        }

        // --- Страны ---
        foreach (CountrySaveDto c in dto.Countries)
        {
            if (c.Id < 0 || c.Id >= world.Countries.Length || world.Countries[c.Id] == null)
                continue;
            CountryData cur = world.Countries[c.Id];
            cur.Code = c.Code; cur.NameKey = c.NameKey;
            cur.GovernmentType = (GovernmentType)c.GovernmentType;
            cur.Ideology = (Ideology)c.Ideology;
            cur.Stability = c.Stability; cur.Legitimacy = c.Legitimacy; cur.WarExhaustion = c.WarExhaustion;
            cur.Treasury = c.Treasury; cur.Debt = c.Debt; cur.Inflation = c.Inflation;
            cur.BaseInterestRate = c.BaseInterestRate; cur.Gdp = c.Gdp; cur.Population = c.Population;
            cur.Urbanization = c.Urbanization; cur.Literacy = c.Literacy; cur.TechLevel = c.TechLevel;
            cur.CreditRating = (CreditRating)c.CreditRating; cur.CurrencyId = c.CurrencyId;
            cur.Laws = c.Laws.ToArray();
            cur.Relations = new Dictionary<int, float>(c.Relations);
            cur.CapitalProvinceId = c.CapitalProvinceId;
            cur.OwnedProvinceIds = new List<int>(c.OwnedProvinceIds);
            cur.ControlledProvinceIds = new List<int>(c.ControlledProvinceIds);
            cur.AiProfile = (AiProfile)c.AiProfile;
            cur.IsPlayer = c.IsPlayer; cur.IsAlive = c.IsAlive;
            cur.FlagId = c.FlagId;
            try { cur.Color = new Godot.Color(c.ColorHex); } catch { }
        }

        // --- Экономика ---
        if (economy.Countries.Length != dto.Economies.Count)
            economy.Allocate(world);
        for (int i = 0; i < dto.Economies.Count && i < economy.Countries.Length; i++)
        {
            EconomySaveDto e = dto.Economies[i];
            CountryEconomy eco = economy.Countries[i];
            eco.LaborForce = e.LaborForce; eco.Employment = e.Employment;
            eco.UnemploymentRate = e.UnemploymentRate; eco.Gdp = e.Gdp;
            eco.GdpPerCapita = e.GdpPerCapita; eco.AvgWage = e.AvgWage;
            eco.BudgetRevenue = e.BudgetRevenue; eco.BudgetExpenses = e.BudgetExpenses;
            eco.Deficit = e.Deficit; eco.TradeBalance = e.TradeBalance;
            eco.Inflation = e.Inflation; eco.InterestRate = e.InterestRate;
            eco.Debt = e.Debt; eco.DebtToGdp = e.DebtToGdp; eco.Reserves = e.Reserves;
            eco.Supply = e.Supply; eco.Demand = e.Demand; eco.Price = e.Price;
            eco.Production = e.Production; eco.Consumption = e.Consumption;
            eco.Taxes.Income = e.TaxIncome; eco.Taxes.Corporate = e.TaxCorporate;
            eco.Taxes.Vat = e.TaxVat; eco.Taxes.Property = e.TaxProperty;
            eco.Taxes.Resource = e.TaxResource; eco.Taxes.ImportTariff = e.TaxImport;
            eco.Taxes.ExportTariff = e.TaxExport; eco.Taxes.Luxury = e.TaxLuxury;
        }

        // --- Армии и войны ---
        armiesOut.Clear();
        foreach (ArmySaveDto a in dto.Armies)
            armiesOut.Add(new ArmyData
            {
                Id = a.Id, OwnerId = a.OwnerId, ProvinceId = a.ProvinceId,
                UnitCounts = new Dictionary<int, int>(a.UnitCounts),
                Strength = a.Strength, Morale = a.Morale, Organization = a.Organization,
                Supply = a.Supply, CommanderId = a.CommanderId, FortLevel = a.FortLevel,
                MoveOrder = new List<int>(a.MoveOrder),
            });

        warsOut.Clear();
        foreach (WarSaveDto w in dto.Wars)
            warsOut.Add(new WarData
            {
                Id = w.Id, AttackerId = w.AttackerId, DefenderId = w.DefenderId,
                AllyIds = new List<int>(w.AllyIds), WarGoals = w.WarGoals,
                StartTurn = w.StartTurn, Battles = w.Battles,
                OccupiedProvinces = new List<int>(w.OccupiedProvinces), WarScore = w.WarScore,
            });
    }
}
