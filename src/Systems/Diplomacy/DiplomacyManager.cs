using System;
using System.Collections.Generic;
using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;

namespace GrandStrategy.Systems.Diplomacy;

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
        WarData? war = FindWar(a, b);
        if (war == null)
            return false;

        Wars.Remove(war);
        SetStatus(a, b, DiplomacyStatus.Truce);
        SetStatus(b, a, DiplomacyStatus.Truce);

        WorldData world = DataManager.Instance.World;
        world.Countries[a].SetRelation(b, world.Countries[a].RelationWith(b) + 20f);
        world.Countries[b].SetRelation(a, world.Countries[b].RelationWith(a) + 20f);

        LogService.Instance.Info($"Diplomacy: peace between {a} and {b}");
        EventBus.Instance.EmitPeaceSigned(a, b);
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
        return true;
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
