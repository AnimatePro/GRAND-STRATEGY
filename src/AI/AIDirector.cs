using System;
using System.Collections.Generic;
using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;
using GrandStrategy.Systems.Diplomacy;
using GrandStrategy.Systems.Economy;
using GrandStrategy.Systems.Military;
using GrandStrategy.Utils;

namespace GrandStrategy.AI;

/// <summary>
/// ИИ-директор (Autoload #14). Utility AI: решения по экономике/дипломатии/военке
/// с весами по профилю личности. Те же правила, что у игрока; без читов.
/// Стратегические решения — раз в N ходов (staggered), экономика — каждый ход.
/// </summary>
public partial class AIDirector : Node
{
    public static AIDirector Instance { get; private set; } = null!;

    private long _seed;
    private readonly Dictionary<int, int> _nextThinkTurn = new();
    private readonly Dictionary<int, int> _lastWarTurn = new();

    public override void _Ready() => Instance = this;

    public void SetSeed(long seed) => _seed = seed;

    public void Reset()
    {
        _nextThinkTurn.Clear();
        _lastWarTurn.Clear();
    }

    public void Tick()
    {
        WorldData world = DataManager.Instance.World;
        var rng = new Rng(_seed + TimeManager.Instance.CurrentTurn);

        for (int c = 0; c < world.CountryCount; c++)
        {
            CountryData country = world.Countries[c];
            if (country == null || !country.IsAlive || country.IsPlayer)
                continue;

            // Экономика — каждый ход.
            ManageEconomy(world, c);

            // Стратегия — раз в 3 хода (staggered по id).
            if (!_nextThinkTurn.TryGetValue(c, out int next))
            {
                next = rng.NextInt(0, 2);
                _nextThinkTurn[c] = next;
            }
            if (TimeManager.Instance.CurrentTurn >= next)
            {
                Think(world, c, rng);
                _nextThinkTurn[c] = TimeManager.Instance.CurrentTurn + 3;
            }
        }
    }

    private void ManageEconomy(WorldData world, int countryId)
    {
        CountryEconomy eco = EconomyManager.Instance.Economy.Countries[countryId];
        if (eco == null)
            return;

        // Балансировка налогов под целевой дефицит.
        if (eco.Deficit > eco.Gdp * 0.02)
            eco.Taxes.Income = Math.Min(eco.Taxes.Income + 0.005, 0.5);
        else if (eco.Deficit < -eco.Gdp * 0.02)
            eco.Taxes.Income = Math.Max(eco.Taxes.Income - 0.005, 0.05);
    }

    private void Think(WorldData world, int countryId, Rng rng)
    {
        CountryData country = world.Countries[countryId];
        AiProfile profile = country.AiProfile;

        switch (profile)
        {
            case AiProfile.Expansionist:
            case AiProfile.Militarist:
                ConsiderWar(world, countryId, rng, aggressive: true);
                ConsiderRecruitment(world, countryId);
                break;
            case AiProfile.Diplomat:
                ConsiderAlliance(world, countryId, rng);
                ConsiderImproveRelations(world, countryId, rng);
                break;
            case AiProfile.Trader:
                ConsiderImproveRelations(world, countryId, rng);
                break;
            case AiProfile.Defensive:
                ConsiderAlliance(world, countryId, rng);
                ConsiderRecruitment(world, countryId);
                break;
            case AiProfile.Opportunist:
                ConsiderWar(world, countryId, rng, aggressive: true);
                break;
            case AiProfile.Balanced:
            default:
                ConsiderImproveRelations(world, countryId, rng);
                ConsiderRecruitment(world, countryId);
                break;
        }

        ConsiderConstruction(world, countryId, rng);
    }

    private void ConsiderWar(WorldData world, int countryId, Rng rng, bool aggressive)
    {
        if (_lastWarTurn.TryGetValue(countryId, out int last) &&
            TimeManager.Instance.CurrentTurn - last < 30)
            return;

        double myPower = MilitaryPower(world, countryId);
        // Ищем слабого соседа.
        int target = -1;
        double bestRatio = 0.0;
        foreach (int pid in world.Countries[countryId].OwnedProvinceIds)
        {
            ProvinceData p = world.GetProvince(pid);
            foreach (int nid in p.NeighborIds)
            {
                ProvinceData n = world.GetProvince(nid);
                if (n.OwnerId < 0 || n.OwnerId == countryId)
                    continue;
                if (DiplomacyManager.Instance.AreAtWar(countryId, n.OwnerId))
                    continue;
                double theirPower = MilitaryPower(world, n.OwnerId);
                double ratio = myPower / Math.Max(theirPower, 1.0);
                double threshold = aggressive ? 1.4 : 2.5;
                if (ratio > threshold && ratio > bestRatio)
                {
                    bestRatio = ratio;
                    target = n.OwnerId;
                }
            }
        }

        if (target >= 0 && rng.Chance(aggressive ? 0.3 : 0.1))
        {
            DiplomacyManager.Instance.DeclareWar(countryId, target, "expansion");
            _lastWarTurn[countryId] = TimeManager.Instance.CurrentTurn;
        }
    }

    private void ConsiderRecruitment(WorldData world, int countryId)
    {
        CountryData country = world.Countries[countryId];
        bool atWar = false;
        foreach (WarData w in DiplomacyManager.Instance.Wars)
            if (w.AttackerId == countryId || w.DefenderId == countryId)
            {
                atWar = true;
                break;
            }

        if (atWar && country.Treasury > 500 && ManpowerAvailable(world, countryId) > 1000)
        {
            int capital = country.CapitalProvinceId;
            if (capital >= 0)
                MilitaryManager.Instance.RecruitArmy(countryId, capital,
                    new Dictionary<int, int> { { 0, 5 }, { 2, 2 } });
        }
    }

    private void ConsiderAlliance(WorldData world, int countryId, Rng rng)
    {
        CountryData country = world.Countries[countryId];
        foreach (int pid in country.OwnedProvinceIds)
        {
            ProvinceData p = world.GetProvince(pid);
            foreach (int nid in p.NeighborIds)
            {
                ProvinceData n = world.GetProvince(nid);
                if (n.OwnerId < 0 || n.OwnerId == countryId)
                    continue;
                if (country.RelationWith(n.OwnerId) > 60 && rng.Chance(0.3))
                {
                    DiplomacyManager.Instance.FormAlliance(countryId, n.OwnerId);
                    return;
                }
            }
        }
    }

    private void ConsiderImproveRelations(WorldData world, int countryId, Rng rng)
    {
        CountryData country = world.Countries[countryId];
        int best = -1;
        double bestGdp = 0;
        foreach (int pid in country.OwnedProvinceIds)
        {
            ProvinceData p = world.GetProvince(pid);
            foreach (int nid in p.NeighborIds)
            {
                ProvinceData n = world.GetProvince(nid);
                if (n.OwnerId < 0 || n.OwnerId == countryId)
                    continue;
                if (world.Countries[n.OwnerId].Gdp > bestGdp)
                {
                    bestGdp = world.Countries[n.OwnerId].Gdp;
                    best = n.OwnerId;
                }
            }
        }
        if (best >= 0)
            DiplomacyManager.Instance.ImproveRelations(countryId, best, 3f);
    }

    private void ConsiderConstruction(WorldData world, int countryId, Rng rng)
    {
        CountryData country = world.Countries[countryId];
        if (country.Treasury < 1000 || rng.Chance(0.7))
            return;
        // Строим в провинции с наименьшей инфраструктурой.
        int worst = -1;
        float worstInfra = float.MaxValue;
        foreach (int pid in country.OwnedProvinceIds)
        {
            ProvinceData p = world.GetProvince(pid);
            if (p.Infrastructure < worstInfra)
            {
                worstInfra = p.Infrastructure;
                worst = pid;
            }
        }
        if (worst >= 0 && worstInfra < 0.8f)
        {
            ProvinceData p = world.GetProvince(worst);
            p.Infrastructure = Mathf.Clamp(p.Infrastructure + 0.05f, 0f, 1f);
            world.SetProvince(worst, in p);
            country.Treasury -= 500;
        }
    }

    private static double MilitaryPower(WorldData world, int countryId)
    {
        double power = ManpowerAvailable(world, countryId) * 0.01;
        foreach (ArmyData army in MilitaryManager.Instance.Armies)
            if (army.OwnerId == countryId)
                power += army.AttackPower(MilitaryManager.UnitTypes) * 10;
        return power;
    }

    private static long ManpowerAvailable(WorldData world, int countryId)
    {
        long mp = 0;
        foreach (int pid in world.Countries[countryId].OwnedProvinceIds)
            mp += world.GetProvince(pid).MaleAdultCount;
        return mp;
    }
}
