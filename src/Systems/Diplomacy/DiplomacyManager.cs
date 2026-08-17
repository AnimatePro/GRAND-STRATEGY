using System;
using System.Collections.Generic;
using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;
using GrandStrategy.Systems.Governance;

namespace GrandStrategy.Systems.Diplomacy;

/// <summary>Условия мирного договора.</summary>
public sealed class PeaceTerms
{
    public bool CedeOccupied;   // передать оккупированные провинции их контролёру
    public bool Puppet;         // проигравший становится вассалом победителя
    public bool Annex;          // полная аннексия проигравшего
    public double Reparations;  // разовый платёж победителю
}

/// <summary>Результат мирного договора.</summary>
public enum PeaceType
{
    WhitePeace = 0,
    Victory = 1,   // атакующий выиграл (по war score)
    Defeat = 2,    // защищающийся выиграл
}

/// <summary>
/// Менеджер дипломатии (Autoload #11). Отношения (-100..100) хранятся в CountryData.Relations,
/// статусы — в матрице (alliance/truce/war/...), войны — список WarData.
/// API: объявление войны, мир, альянсы, улучшение отношений, AreAtWar.
/// </summary>
public partial class DiplomacyManager : Node
{
    public static DiplomacyManager Instance { get; private set; } = null!;

    public List<WarData> Wars { get; private set; } = new();
    private readonly Dictionary<(int, int), DiplomacyStatus> _status = new();

    private int _nextWarId = 1;

    public override void _Ready() => Instance = this;

    public void Reset()
    {
        Wars.Clear();
        _status.Clear();
        _nextWarId = 1;
    }

    /// <summary>Восстановление войн из сохранения (с пересборкой матрицы статусов).</summary>
    public void RestoreWars(List<WarData> wars)
    {
        Wars.Clear();
        _status.Clear();
        Wars.AddRange(wars);
        foreach (WarData w in Wars)
            SetStatus(w.AttackerId, w.DefenderId, DiplomacyStatus.War);
        _nextWarId = Wars.Count + 1;
    }

    // --- Статусы -------------------------------------------------------------

    public DiplomacyStatus GetStatus(int a, int b)
    {
        if (a == b)
            return DiplomacyStatus.Neutral;
        return _status.TryGetValue(Key(a, b), out DiplomacyStatus s) ? s : DiplomacyStatus.Neutral;
    }

    public void SetStatus(int a, int b, DiplomacyStatus status)
    {
        if (a == b)
            return;
        _status[Key(a, b)] = status;
    }

    public bool AreAtWar(int a, int b)
    {
        if (a == b)
            return false;
        foreach (WarData w in Wars)
        {
            if ((w.AttackerId == a && w.DefenderId == b) || (w.AttackerId == b && w.DefenderId == a))
                return true;
        }
        return false;
    }

    public WarData? FindWar(int a, int b)
    {
        foreach (WarData w in Wars)
            if ((w.AttackerId == a && w.DefenderId == b) || (w.AttackerId == b && w.DefenderId == a))
                return w;
        return null;
    }

    // --- Действия ------------------------------------------------------------

    public bool DeclareWar(int attackerId, int defenderId, string warGoals = "")
    {
        if (attackerId == defenderId || AreAtWar(attackerId, defenderId))
            return false;

        var war = new WarData
        {
            Id = _nextWarId++,
            AttackerId = attackerId,
            DefenderId = defenderId,
            WarGoals = warGoals,
            StartTurn = TimeManager.Instance.CurrentTurn,
        };
        Wars.Add(war);
        SetStatus(attackerId, defenderId, DiplomacyStatus.War);

        WorldData world = DataManager.Instance.World;
        world.Countries[attackerId].SetRelation(defenderId, -100f);
        world.Countries[defenderId].SetRelation(attackerId, -100f);

        LogService.Instance.Info($"Diplomacy: {attackerId} declared war on {defenderId} ({warGoals})");
        EventBus.Instance.EmitWarDeclared(attackerId, defenderId);
        return true;
    }

    public bool SignPeace(int a, int b)
    {
        return MakePeace(a, b, new PeaceTerms());
    }

    /// <summary>Мирный договор с условиями. a = инициатор (обычно победитель).</summary>
    public bool MakePeace(int a, int b, PeaceTerms terms)
    {
        WarData? war = FindWar(a, b);
        if (war == null)
            return false;

        WorldData world = DataManager.Instance.World;
        CountryData ca = world.Countries[a];
        CountryData cb = world.Countries[b];

        // Победитель: по war score (с точки зрения атакующего).
        double score = war.WarScore;
        int winner = score >= 0 ? war.AttackerId : war.DefenderId;
        int loser = score >= 0 ? war.DefenderId : war.AttackerId;
        CountryData cw = world.Countries[winner];
        CountryData cl = world.Countries[loser];

        // Уступка оккупированных провинций их контролёру.
        if (terms.CedeOccupied)
        {
            foreach (int pid in new List<int>(war.OccupiedProvinces))
            {
                ProvinceData p = world.GetProvince(pid);
                if (p.ControllerId >= 0 && p.ControllerId != p.OwnerId)
                {
                    GovernanceSystem.TransferProvince(world, pid, p.OwnerId, p.ControllerId);
                }
            }
        }

        // Вассалитет проигравшего.
        if (terms.Puppet)
        {
            SetStatus(winner, loser, DiplomacyStatus.Vassal);
            SetStatus(loser, winner, DiplomacyStatus.Vassal);
            cl.AiProfile = AiProfile.Defensive;
        }

        // Аннексия проигравшего.
        if (terms.Annex)
        {
            foreach (int pid in new List<int>(cl.OwnedProvinceIds))
            {
                ProvinceData p = world.GetProvince(pid);
                p.OwnerId = winner;
                p.ControllerId = -1;
                world.SetProvince(pid, in p);
                cw.OwnedProvinceIds.Add(pid);
            }
            cl.IsAlive = false;
            cl.OwnedProvinceIds.Clear();
            cl.ControlledProvinceIds.Clear();
        }

        // Репарации.
        if (terms.Reparations > 0)
        {
            double pay = Math.Min(terms.Reparations, cl.Treasury);
            cl.Treasury -= pay;
            cw.Treasury += pay;
        }

        Wars.Remove(war);
        SetStatus(a, b, DiplomacyStatus.Truce);
        SetStatus(b, a, DiplomacyStatus.Truce);

        ca.SetRelation(b, ca.RelationWith(b) + 20f);
        cb.SetRelation(a, cb.RelationWith(a) + 20f);
        ca.WarExhaustion = 0f;
        cb.WarExhaustion = 0f;

        LogService.Instance.Info($"Diplomacy: peace between {a} and {b} (winner {winner})");
        EventBus.Instance.EmitPeaceSigned(a, b);
        EventBus.Instance.EmitDiplomacyUpdated();
        return true;
    }

    public void ImproveRelations(int a, int b, float amount = 5f)
    {
        WorldData world = DataManager.Instance.World;
        world.Countries[a].SetRelation(b, world.Countries[a].RelationWith(b) + amount);
    }

    public void Insult(int a, int b)
    {
        WorldData world = DataManager.Instance.World;
        world.Countries[a].SetRelation(b, world.Countries[a].RelationWith(b) - 10f);
    }

    public bool FormAlliance(int a, int b)
    {
        if (AreAtWar(a, b) || GetStatus(a, b) == DiplomacyStatus.Alliance)
            return false;
        SetStatus(a, b, DiplomacyStatus.Alliance);
        ImproveRelations(a, b, 25f);
        ImproveRelations(b, a, 25f);
        EventBus.Instance.EmitDiplomacyUpdated();
        return true;
    }

    /// <summary>Переключение эмбарго (a вводит/снимает эмбарго против b).</summary>
    public void ToggleEmbargo(int a, int b)
    {
        WorldData world = DataManager.Instance.World;
        var set = world.Countries[a].TradePolicy.Embargoed;
        if (!set.Remove(b))
            set.Add(b);
        EventBus.Instance.EmitTradeUpdated();
    }

    /// <summary>Циклический сдвиг торгового соглашения a с b.</summary>
    public void CycleTradeAgreement(int a, int b)
    {
        WorldData world = DataManager.Instance.World;
        var agreements = world.Countries[a].TradePolicy.Agreements;
        agreements.TryGetValue(b, out TradeAgreement cur);
        TradeAgreement next = cur switch
        {
            TradeAgreement.None => TradeAgreement.FreeTrade,
            TradeAgreement.FreeTrade => TradeAgreement.Preferential,
            TradeAgreement.Preferential => TradeAgreement.CustomsUnion,
            TradeAgreement.CustomsUnion => TradeAgreement.CommonMarket,
            _ => TradeAgreement.None,
        };
        if (next == TradeAgreement.None)
            agreements.Remove(b);
        else
            agreements[b] = next;
        EventBus.Instance.EmitTradeUpdated();
    }

    public TradeAgreement GetTradeAgreement(int a, int b)
    {
        WorldData world = DataManager.Instance.World;
        return world.Countries[a].TradePolicy.Agreements.TryGetValue(b, out TradeAgreement v)
            ? v : TradeAgreement.None;
    }

    // --- Тик -------------------------------------------------------------

    public void Tick()
    {
        // Дрейф отношений к нейтрали.
        WorldData world = DataManager.Instance.World;
        for (int a = 0; a < world.CountryCount; a++)
        {
            CountryData ca = world.Countries[a];
            if (ca == null || !ca.IsAlive)
                continue;
            var keys = new List<int>(ca.Relations.Keys);
            foreach (int b in keys)
            {
                if (AreAtWar(a, b))
                    continue; // воюющие отношения не дрейфуют
                float v = ca.RelationWith(b);
                float drift = v > 0 ? 0.01f : v < 0 ? -0.01f : 0f;
                ca.SetRelation(b, v - drift);
            }
        }

        // Военное истощение растёт у воюющих.
        foreach (WarData w in Wars)
        {
            world.Countries[w.AttackerId].WarExhaustion =
                Mathf.Clamp(world.Countries[w.AttackerId].WarExhaustion + 0.01f, 0f, 100f);
            world.Countries[w.DefenderId].WarExhaustion =
                Mathf.Clamp(world.Countries[w.DefenderId].WarExhaustion + 0.01f, 0f, 100f);
        }
    }

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
}
