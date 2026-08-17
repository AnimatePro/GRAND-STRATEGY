using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;
using GrandStrategy.Utils;

namespace GrandStrategy.Systems.Governance;

/// <summary>
/// Политическая динамика: смена правительства/идеологии, перевороты при низкой
/// стабильности и легитимности, мятежи/сепаратизм при высоком Unrest.
/// Статический (не синглтон) — вызывается из GameManager.EndTurn.
/// </summary>
public static class GovernanceSystem
{
    public static readonly GovernmentType[] GovernmentOrder =
    {
        GovernmentType.Monarchy,
        GovernmentType.Republic,
        GovernmentType.ConstitutionalMonarchy,
        GovernmentType.ParliamentaryRepublic,
        GovernmentType.PresidentialRepublic,
        GovernmentType.Theocracy,
        GovernmentType.Dictatorship,
        GovernmentType.Tribal,
    };

    public static readonly Ideology[] IdeologyOrder =
    {
        Ideology.Liberalism,
        Ideology.Conservatism,
        Ideology.Socialism,
        Ideology.Communism,
        Ideology.Fascism,
        Ideology.Nationalism,
        Ideology.Theocracy,
    };

    public static void Tick(WorldData world, Rng rng)
    {
        for (int c = 0; c < world.CountryCount; c++)
        {
            CountryData country = world.Countries[c];
            if (country == null || !country.IsAlive)
                continue;
            TickCountry(world, country, rng);
        }
    }

    private static void TickCountry(WorldData world, CountryData country, Rng rng)
    {
        // --- Переворот при коллапсе легитимности/стабильности ---
        if (country.Stability < 15f && country.Legitimacy < 15f && rng.Chance(0.05))
        {
            country.GovernmentType = GovernmentOrder[rng.NextInt(0, GovernmentOrder.Length - 1)];
            country.Ideology = IdeologyOrder[rng.NextInt(0, IdeologyOrder.Length - 1)];
            country.Stability = 30f;
            country.Legitimacy = 25f;
            LogService.Instance.Warning($"Governance: coup in {country.Code}");
        }

        // --- Мятежи/сепаратизм по провинциям ---
        foreach (int pid in country.OwnedProvinceIds)
        {
            int idx = world.ProvinceIdToIndex[pid];
            ProvinceData p = world.Provinces[idx];
            if (p.Unrest < 0.3f)
                continue;

            double unrest = p.Unrest;
            // Ущерб инфраструктуре и развитию.
            if (rng.Chance(unrest * 0.05))
            {
                p.Infrastructure = Mathf.Clamp(p.Infrastructure - 0.02f, 0f, 1f);
                p.Development = Mathf.Clamp(p.Development - 0.01f, 0f, 1f);
                country.Stability = Mathf.Clamp(country.Stability - 0.5f, 0f, 100f);
            }

            // Сепаратизм: ядро другой страны + высокий unrest -> переход.
            if (unrest > 0.7f && rng.Chance(0.02))
            {
                int separatist = -1;
                foreach (int coreId in p.CoreIds)
                {
                    if (coreId != country.Id && coreId >= 0 && coreId < world.Countries.Length &&
                        world.Countries[coreId] != null && world.Countries[coreId].IsAlive)
                    {
                        separatist = coreId;
                        break;
                    }
                }
                if (separatist >= 0)
                {
                    TransferProvince(world, pid, country.Id, separatist);
                    LogService.Instance.Warning($"Governance: province {pid} seceded to {separatist}");
                }
            }

            world.Provinces[idx] = p;
        }
    }

    public static void TransferProvince(WorldData world, int provinceId, int fromCountry, int toCountry)
    {
        ProvinceData p = world.GetProvince(provinceId);
        p.OwnerId = toCountry;
        p.ControllerId = -1;
        p.Unrest = Mathf.Clamp(p.Unrest - 0.3f, 0f, 1f);
        world.SetProvince(provinceId, in p);

        CountryData from = world.Countries[fromCountry];
        from.OwnedProvinceIds.Remove(provinceId);
        from.ControlledProvinceIds.Remove(provinceId);

        CountryData to = world.Countries[toCountry];
        if (!to.OwnedProvinceIds.Contains(provinceId))
            to.OwnedProvinceIds.Add(provinceId);
    }

    /// <summary>Смена формы правления (циклически), с политической ценой.</summary>
    public static void ChangeGovernment(WorldData world, int countryId)
    {
        CountryData country = world.Countries[countryId];
        int cur = System.Array.IndexOf(GovernmentOrder, country.GovernmentType);
        int next = (cur + 1) % GovernmentOrder.Length;
        country.GovernmentType = GovernmentOrder[next];
        country.Stability = Mathf.Clamp(country.Stability - 10f, 0f, 100f);
        country.Legitimacy = Mathf.Clamp(country.Legitimacy - 5f, 0f, 100f);
    }

    public static void ChangeIdeology(WorldData world, int countryId)
    {
        CountryData country = world.Countries[countryId];
        int cur = System.Array.IndexOf(IdeologyOrder, country.Ideology);
        int next = (cur + 1) % IdeologyOrder.Length;
        country.Ideology = IdeologyOrder[next];
        country.Stability = Mathf.Clamp(country.Stability - 5f, 0f, 100f);
    }
}
