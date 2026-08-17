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
/// ИИ-директор (Autoload). Utility AI «почти как человек»: каждый ИИ-игрок имеет профиль
/// и преследует цель, реагирует на угрозы, не воюет суицидально, заключает мир при
/// безнадёжной войне, держит армию пропорционально ВВП и угрозе, управляет бюджетом
/// с учётом инфляции, строит и принимает законы по профилю. Те же правила, что у игрока.
/// </summary>
public partial class AIDirector : Node
{
    public static AIDirector Instance { get; private set; } = null!;

    private long _seed;
    private readonly Dictionary<int, int> _nextThinkTurn = new();
    private readonly Dictionary<int, int> _lastWarTurn = new();
    private readonly Dictionary<int, int> _lastPeaceTurn = new();

    public override void _Ready() => Instance = this;

    public void SetSeed(long seed) => _seed = seed;

    public void Reset()
    {
        _nextThinkTurn.Clear();
        _lastWarTurn.Clear();
        _lastPeaceTurn.Clear();
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

            ManageEconomy(world, c);

            // Армии в войне двигаются каждый ход (не только при стратегическом тике).
            ConsiderMoves(world, c);

            if (!_nextThinkTurn.TryGetValue(c, out int next))
                _nextThinkTurn[c] = next = rng.NextInt(0, 2);
            if (TimeManager.Instance.CurrentTurn >= next)
            {
                Think(world, c, rng);
                _nextThinkTurn[c] = TimeManager.Instance.CurrentTurn + 3;
            }
        }
    }

    /// <summary>ИИ отдаёт приказы движения своим армиям к вражеским провинциям.</summary>
    private void ConsiderMoves(WorldData world, int countryId)
    {
        // Собираем врагов (противоположная сторона всех войн).
        var enemies = new HashSet<int>();
        foreach (WarData w in DiplomacyManager.Instance.Wars)
        {
            int side = WarSide(w, countryId);
            if (side == 0)
                continue;
            if (side > 0)
            {
                enemies.Add(w.DefenderId);
                foreach (int a in w.AllyIds) enemies.Add(a);
            }
            else
            {
                enemies.Add(w.AttackerId);
                foreach (int a in w.AttackerAllies) enemies.Add(a);
            }
        }
        if (enemies.Count == 0)
            return;

        foreach (ArmyData army in MilitaryManager.Instance.Armies)
        {
            if (army.OwnerId != countryId || army.MoveOrder.Count > 0)
                continue;

            // Отступление разбитой/истощённой армии в дружественную провинцию.
            if (army.Strength < 0.3f || army.Morale < 0.3f || army.Supply < 15.0)
            {
                int retreat = NearestOwned(world, countryId, army.ProvinceId);
                if (retreat >= 0 && retreat != army.ProvinceId)
                {
                    MilitaryManager.Instance.OrderMove(army.Id, retreat);
                    continue;
                }
            }

            // Ближайшая провинция врага (морской десант тоже работает: BFS по морю).
            int target = -1;
            float bestDist = float.MaxValue;
            Vector2 myPos = world.GetProvince(army.ProvinceId).Centroid;
            foreach (int e in enemies)
            {
                CountryData ec = world.GetCountry(e);
                if (ec == null || !ec.IsAlive)
                    continue;
                foreach (int pid in ec.OwnedProvinceIds)
                {
                    float d = myPos.DistanceTo(world.GetProvince(pid).Centroid);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        target = pid;
                    }
                }
            }
            if (target >= 0)
                MilitaryManager.Instance.OrderMove(army.Id, target);
        }
    }

    /// <summary>Ближайшая дружественная (своя) провинция для отступления.</summary>
    private static int NearestOwned(WorldData world, int countryId, int fromProvinceId)
    {
        CountryData country = world.GetCountry(countryId);
        Vector2 myPos = world.GetProvince(fromProvinceId).Centroid;
        int best = -1;
        float bestDist = float.MaxValue;
        foreach (int pid in country.OwnedProvinceIds)
        {
            if (pid == fromProvinceId)
                continue;
            float d = myPos.DistanceTo(world.GetProvince(pid).Centroid);
            if (d < bestDist)
            {
                bestDist = d;
                best = pid;
            }
        }
        return best;
    }

    // ===================== ЭКОНОМИКА (каждый ход) =====================

    private void ManageEconomy(WorldData world, int countryId)
    {
        CountryData country = world.Countries[countryId];
        CountryEconomy eco = EconomyManager.Instance.Economy.Countries[countryId];
        if (eco == null)
            return;

        // Налоговая политика: подгоняем к сбалансированному бюджету.
        if (eco.Deficit > eco.Gdp * 0.02)
            eco.Taxes.Income = Math.Min(eco.Taxes.Income + 0.01, 0.5);
        else if (eco.Deficit < -eco.Gdp * 0.05) // большой профицит — снижаем налоги (стимул роста)
            eco.Taxes.Income = Math.Max(eco.Taxes.Income - 0.005, 0.05);

        // При высокой инфляции (>20% в год) урезаем расходы на благосостояние.
        if (eco.Inflation > 0.20)
            eco.Spending.Welfare = Math.Max(eco.Spending.Welfare - 0.01, 0.02);
        else if (eco.Inflation < 0.05 && eco.Deficit < 0)
            eco.Spending.Welfare = Math.Min(eco.Spending.Welfare + 0.005, 0.25);

        // При долговой нагрузке выше 100% — ужесточаем бюджет (срезаем субсидии).
        if (eco.DebtToGdp > 1.0)
            eco.Spending.Subsidies = Math.Max(eco.Spending.Subsidies - 0.01, 0.0);
    }

    // ===================== СТРАТЕГИЯ (раз в 3 хода) =====================

    private void Think(WorldData world, int countryId, Rng rng)
    {
        CountryData country = world.Countries[countryId];

        // 1. Мир при безнадёжной/затяжной войне — критичное решение.
        if (ConsiderPeace(world, countryId))
            return;

        // 2. Угроза от соседей (общие границы, плохие отношения/война).
        double threat = ComputeThreat(world, countryId);

        // 3. Профильные действия.
        switch (country.AiProfile)
        {
            case AiProfile.Expansionist:
            case AiProfile.Militarist:
            case AiProfile.Opportunist:
                ConsiderWar(world, countryId, rng, aggressive: true);
                break;
            case AiProfile.Diplomat:
                ConsiderAlliance(world, countryId, rng, threat);
                ConsiderImproveRelations(world, countryId, rng);
                break;
            case AiProfile.Trader:
                ConsiderImproveRelations(world, countryId, rng);
                ConsiderBuildTrade(world, countryId, rng);
                break;
            case AiProfile.Defensive:
            case AiProfile.Isolationist:
                ConsiderAlliance(world, countryId, rng, threat);
                ConsiderFortify(world, countryId, rng);
                break;
            case AiProfile.Balanced:
            default:
                ConsiderImproveRelations(world, countryId, rng);
                break;
        }

        // 4. Общее: армия, строительство, законы, дип.поза (все профили).
        ConsiderRecruitment(world, countryId, threat);
        ConsiderConstruction(world, countryId, rng);
        ConsiderLaws(world, countryId);
        ConsiderDiplomaticStance(world, countryId, rng);
    }

    /// <summary>Дипломатическая поза: эмбарго заклятым врагам, оскорбления соперникам, разрядка с угрозой.</summary>
    private void ConsiderDiplomaticStance(WorldData world, int countryId, Rng rng)
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
                int other = n.OwnerId;
                if (DiplomacyManager.Instance.AreAtWar(countryId, other))
                    continue;

                float rel = country.RelationWith(other);

                // Заклятый враг -> эмбарго (с ухудшением отношений).
                if (rel < -60 && !country.TradePolicy.Embargoed.Contains(other) && rng.Chance(0.3))
                {
                    country.TradePolicy.Embargoed.Add(other);
                    DiplomacyManager.Instance.ChangeRelation(countryId, other, -5f, "REL_EMBARGO");
                }
                // Соперник (не враг) -> агрессивные профили оскорбляют (демонстрация силы).
                else if (rel < -30 && rng.Chance(0.2) &&
                         country.AiProfile is AiProfile.Militarist or AiProfile.Expansionist or AiProfile.Opportunist)
                {
                    DiplomacyManager.Instance.Insult(countryId, other);
                }
            }
        }
    }

    // ===================== УГРОЗА =====================

    /// <summary>Суммарная военная угроза: сила соседей с плохими отношениями/войной.</summary>
    private double ComputeThreat(WorldData world, int countryId)
    {
        double threat = 0.0;
        var seen = new HashSet<int>();
        foreach (int pid in world.Countries[countryId].OwnedProvinceIds)
        {
            ProvinceData p = world.GetProvince(pid);
            foreach (int nid in p.NeighborIds)
            {
                ProvinceData n = world.GetProvince(nid);
                if (n.OwnerId < 0 || n.OwnerId == countryId || !seen.Add(n.OwnerId))
                    continue;
                CountryData other = world.Countries[n.OwnerId];
                float rel = other.RelationWith(countryId);
                bool atWar = DiplomacyManager.Instance.AreAtWar(countryId, n.OwnerId);
                if (rel < -20 || atWar)
                {
                    double w = rel < -50 || atWar ? 1.5 : 1.0;
                    threat += MilitaryPower(world, n.OwnerId) * w;
                }
            }
        }
        return threat;
    }

    // ===================== МИР =====================

    private bool ConsiderPeace(WorldData world, int countryId)
    {
        if (_lastPeaceTurn.TryGetValue(countryId, out int lp) &&
            TimeManager.Instance.CurrentTurn - lp < 5)
            return false;

        foreach (WarData war in DiplomacyManager.Instance.Wars)
        {
            int side = WarSide(war, countryId);
            if (side == 0)
                continue;

            // score — с точки зрения атакующего (>0 атакующий выигрывает).
            double myScore = side > 0 ? war.WarScore : -war.WarScore;
            int turns = TimeManager.Instance.CurrentTurn - war.StartTurn;

            bool hopeless = myScore < -30;
            bool stalemate = turns > 60 && Math.Abs(war.WarScore) < 10;

            if (hopeless || stalemate)
            {
                int other = side > 0 ? war.DefenderId : war.AttackerId;
                DiplomacyManager.Instance.MakePeace(countryId, other, new PeaceTerms());
                _lastPeaceTurn[countryId] = TimeManager.Instance.CurrentTurn;
                LogService.Instance.Info($"AI: {countryId} sued for peace ({hopeless ? "hopeless" : "stalemate"})");
                return true;
            }
        }
        return false;
    }

    /// <summary>Сторона страны в войне: 1 = атакующая, -1 = обороняющаяся, 0 = не участвует.</summary>
    private static int WarSide(WarData war, int countryId)
    {
        if (war.AttackerId == countryId || war.AttackerAllies.Contains(countryId))
            return 1;
        if (war.DefenderId == countryId || war.AllyIds.Contains(countryId))
            return -1;
        return 0;
    }

    // ===================== ВОЙНА =====================

    private void ConsiderWar(WorldData world, int countryId, Rng rng, bool aggressive)
    {
        CountryData country = world.Countries[countryId];

        if (_lastWarTurn.TryGetValue(countryId, out int last) &&
            TimeManager.Instance.CurrentTurn - last < 30)
            return;
        if (country.WarExhaustion > 50f)
            return; // устали воевать

        double myPower = MilitaryPower(world, countryId);
        int target = -1;
        double bestRatio = 0.0;

        foreach (int pid in country.OwnedProvinceIds)
        {
            ProvinceData p = world.GetProvince(pid);
            foreach (int nid in p.NeighborIds)
            {
                ProvinceData n = world.GetProvince(nid);
                if (n.OwnerId < 0 || n.OwnerId == countryId)
                    continue;
                if (DiplomacyManager.Instance.AreAtWar(countryId, n.OwnerId))
                    continue;
                // Не атакуем союзников и тех, с кем перемирие.
                DiplomacyStatus status = DiplomacyManager.Instance.GetStatus(countryId, n.OwnerId);
                if (status is DiplomacyStatus.Alliance or DiplomacyStatus.Truce
                    or DiplomacyStatus.Vassal or DiplomacyStatus.Puppet)
                    continue;

                // Сила противника с учётом его союзников (коалиция).
                double theirPower = MilitaryPower(world, n.OwnerId);
                foreach (int ally in AllyIdsOf(world, n.OwnerId))
                    theirPower += MilitaryPower(world, ally) * 0.5;

                double ratio = myPower / Math.Max(theirPower, 1.0);
                double threshold = aggressive ? 1.4 : 2.5;
                if (ratio > threshold && ratio > bestRatio)
                {
                    bestRatio = ratio;
                    target = n.OwnerId;
                }
            }
        }

        if (target >= 0 && rng.Chance(aggressive ? 0.3 : 0.15))
        {
            DiplomacyManager.Instance.DeclareWar(countryId, target, "expansion");
            _lastWarTurn[countryId] = TimeManager.Instance.CurrentTurn;
        }
    }

    private static IEnumerable<int> AllyIdsOf(WorldData world, int countryId)
    {
        for (int c = 0; c < world.CountryCount; c++)
            if (c != countryId && world.Countries[c] != null && world.Countries[c].IsAlive &&
                DiplomacyManager.Instance.GetStatus(countryId, c) == DiplomacyStatus.Alliance)
                yield return c;
    }

    // ===================== АРМИЯ =====================

    private void ConsiderRecruitment(WorldData world, int countryId, double threat)
    {
        CountryData country = world.Countries[countryId];
        bool atWar = IsAtWar(world, countryId);
        int armyCount = MilitaryManager.Instance.Armies.Count(a => a.OwnerId == countryId);

        // Целевой размер армии: база от ВВП + надбавка за угрозу/войну.
        double baseArmies = Math.Clamp(country.Gdp / 2_000_000_000.0, 1.0, 25.0);
        double threatArmies = threat / 500.0;
        int target = (int)(baseArmies + threatArmies + (atWar ? 10 : 0));

        bool wantsStanding = country.AiProfile is AiProfile.Militarist or AiProfile.Expansionist
            or AiProfile.Opportunist or AiProfile.Defensive;
        if (!wantsStanding && !atWar)
            target = Math.Min(target, 2); // мирные профили держат мало

        if (armyCount >= target)
            return;
        if (country.Treasury < 500)
            return;
        if (ManpowerAvailable(world, countryId) < 1000)
            return;
        if (armyCount >= 40)
            return; // аппаратный предел, чтобы не перегружать

        int capital = country.CapitalProvinceId;
        if (capital >= 0)
        {
            var units = atWar
                ? new Dictionary<int, int> { { 0, 5 }, { 2, 2 } }
                : new Dictionary<int, int> { { 0, 4 } };
            MilitaryManager.Instance.RecruitArmy(countryId, capital, units);
        }
    }

    private static bool IsAtWar(WorldData world, int countryId)
    {
        foreach (WarData w in DiplomacyManager.Instance.Wars)
            if (WarSide(w, countryId) != 0)
                return true;
        return false;
    }

    // ===================== ДИПЛОМАТИЯ =====================

    private void ConsiderAlliance(WorldData world, int countryId, Rng rng, double threat)
    {
        CountryData country = world.Countries[countryId];
        if (threat < 100 || rng.Chance(0.5))
            return;

        // При угрозе ищем самого сильного соседа с хорошими отношениями.
        int best = -1;
        double bestPower = 0;
        foreach (int pid in country.OwnedProvinceIds)
        {
            ProvinceData p = world.GetProvince(pid);
            foreach (int nid in p.NeighborIds)
            {
                ProvinceData n = world.GetProvince(nid);
                if (n.OwnerId < 0 || n.OwnerId == countryId)
                    continue;
                if (DiplomacyManager.Instance.AreAtWar(countryId, n.OwnerId))
                    continue;
                if (country.RelationWith(n.OwnerId) > 40)
                {
                    double power = MilitaryPower(world, n.OwnerId);
                    if (power > bestPower)
                    {
                        bestPower = power;
                        best = n.OwnerId;
                    }
                }
            }
        }
        if (best >= 0 && DiplomacyManager.Instance.GetStatus(countryId, best) != DiplomacyStatus.Alliance)
            DiplomacyManager.Instance.FormAlliance(countryId, best);
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
                if (DiplomacyManager.Instance.AreAtWar(countryId, n.OwnerId))
                    continue;
                if (world.Countries[n.OwnerId].Gdp > bestGdp && country.RelationWith(n.OwnerId) < 80)
                {
                    bestGdp = world.Countries[n.OwnerId].Gdp;
                    best = n.OwnerId;
                }
            }
        }
        if (best >= 0)
            DiplomacyManager.Instance.ImproveRelations(countryId, best, 3f);
    }

    // ===================== СТРОИТЕЛЬСТВО =====================

    private void ConsiderConstruction(WorldData world, int countryId, Rng rng)
    {
        CountryData country = world.Countries[countryId];
        if (country.Treasury < 1000 || rng.Chance(0.5))
            return;

        int worst = -1;
        float worstDev = float.MaxValue;
        foreach (int pid in country.OwnedProvinceIds)
        {
            ProvinceData p = world.GetProvince(pid);
            if (p.Development < worstDev)
            {
                worstDev = p.Development;
                worst = pid;
            }
        }
        if (worst >= 0)
        {
            int building = rng.Chance(0.5) ? 1 : 9; // ферма / дорога
            MilitaryManager.Instance.BuildBuilding(countryId, worst, building);
        }
    }

    private void ConsiderBuildTrade(WorldData world, int countryId, Rng rng)
    {
        CountryData country = world.Countries[countryId];
        if (country.Treasury < 1000 || rng.Chance(0.5))
            return;
        // Порт в прибрежной провинции.
        foreach (int pid in country.OwnedProvinceIds)
        {
            ProvinceData p = world.GetProvince(pid);
            if (p.IsCoastal)
            {
                MilitaryManager.Instance.BuildBuilding(countryId, pid, 4); // порт
                return;
            }
        }
    }

    private void ConsiderFortify(WorldData world, int countryId, Rng rng)
    {
        CountryData country = world.Countries[countryId];
        if (country.Treasury < 1500 || rng.Chance(0.5))
            return;
        int capital = country.CapitalProvinceId;
        if (capital >= 0 && world.GetProvince(capital).FortLevel < 5)
            MilitaryManager.Instance.BuildFort(countryId, capital);
    }

    // ===================== ЗАКОНЫ =====================

    private void ConsiderLaws(WorldData world, int countryId)
    {
        CountryData country = world.Countries[countryId];
        if (world.Laws.Length == 0)
            return;

        int desiredLaw = country.AiProfile switch
        {
            AiProfile.Trader => LawByName(world, "LAW_FREE_TRADE"),
            AiProfile.Militarist or AiProfile.Expansionist => LawByName(world, "LAW_CONSCRIPTION"),
            AiProfile.Isolationist => LawByName(world, "LAW_PROTECTIONISM"),
            AiProfile.Defensive => LawByName(world, "LAW_PROFESSIONAL_ARMY"),
            _ => -1,
        };
        if (desiredLaw >= 0 && System.Array.IndexOf(country.Laws, desiredLaw) < 0)
        {
            var list = new List<int>(country.Laws) { desiredLaw };
            country.Laws = list.ToArray();
        }
    }

    private static int LawByName(WorldData world, string nameKey)
    {
        for (int i = 0; i < world.Laws.Length; i++)
            if (world.Laws[i].NameKey == nameKey)
                return i;
        return -1;
    }

    // ===================== УТИЛИТЫ =====================

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
