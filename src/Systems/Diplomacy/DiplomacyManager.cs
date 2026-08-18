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
            bool aAttacker = IsAttackerSide(w, a);
            bool bAttacker = IsAttackerSide(w, b);
            bool aDefender = IsDefenderSide(w, a);
            bool bDefender = IsDefenderSide(w, b);
            // Разные стороны => воюют.
            if ((aAttacker && bDefender) || (aDefender && bAttacker))
                return true;
        }
        return false;
    }

    public WarData? FindWar(int a, int b)
    {
        foreach (WarData w in Wars)
        {
            bool aAttacker = IsAttackerSide(w, a);
            bool bAttacker = IsAttackerSide(w, b);
            bool aDefender = IsDefenderSide(w, a);
            bool bDefender = IsDefenderSide(w, b);
            if ((aAttacker && bDefender) || (aDefender && bAttacker))
                return w;
        }
        return null;
    }

    private static bool IsAttackerSide(WarData w, int c) =>
        c == w.AttackerId || w.AttackerAllies.Contains(c);

    private static bool IsDefenderSide(WarData w, int c) =>
        c == w.DefenderId || w.AllyIds.Contains(c);

    // --- Действия ------------------------------------------------------------

    public bool DeclareWar(int attackerId, int defenderId, string warGoals = "")
    {
        if (attackerId == defenderId || AreAtWar(attackerId, defenderId))
            return false;

        WorldData world = DataManager.Instance.World;
        CountryData attacker = world.GetCountry(attackerId);
        CountryData defender = world.GetCountry(defenderId);
        if (attacker == null || defender == null || !attacker.IsAlive || !defender.IsAlive)
            return false; // нельзя воевать с мёртвой страной

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

        ChangeRelation(attackerId, defenderId, -100f, "REL_WAR");
        ChangeRelation(defenderId, attackerId, -100f, "REL_WAR");

        // Коалиции: союзники защищающегося встают на его сторону, союзники атакующего — на его.
        for (int c = 0; c < world.CountryCount; c++)
        {
            if (c == attackerId || c == defenderId)
                continue;
            CountryData cc = world.Countries[c];
            if (cc == null || !cc.IsAlive)
                continue;
            if (GetStatus(c, defenderId) == DiplomacyStatus.Alliance && !AreAtWar(c, attackerId))
            {
                war.AllyIds.Add(c);
                SetStatus(c, attackerId, DiplomacyStatus.War);
                ChangeRelation(c, attackerId, -100f, "REL_WAR");
            }
            else if (GetStatus(c, attackerId) == DiplomacyStatus.Alliance && !AreAtWar(c, defenderId))
            {
                war.AttackerAllies.Add(c);
                SetStatus(c, defenderId, DiplomacyStatus.War);
                ChangeRelation(c, defenderId, -100f, "REL_WAR");
            }
        }

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

        // Перемирие со всеми участниками коалиции.
        foreach (int ally in war.AllyIds)
        {
            SetStatus(ally, war.AttackerId, DiplomacyStatus.Truce);
            SetStatus(war.AttackerId, ally, DiplomacyStatus.Truce);
        }
        foreach (int ally in war.AttackerAllies)
        {
            SetStatus(ally, war.DefenderId, DiplomacyStatus.Truce);
            SetStatus(war.DefenderId, ally, DiplomacyStatus.Truce);
        }

        ChangeRelation(a, b, 20f, "REL_PEACE");
        ChangeRelation(b, a, 20f, "REL_PEACE");
        ca.WarExhaustion = 0f;
        cb.WarExhaustion = 0f;

        LogService.Instance.Info($"Diplomacy: peace between {a} and {b} (winner {winner})");
        EventBus.Instance.EmitPeaceSigned(a, b);
        EventBus.Instance.EmitDiplomacyUpdated();
        return true;
    }

    // --- Отношения (с причиной) ---------------------------------------------

    /// <summary>
    /// Единая точка изменения отношений. Отношения НЕ меняются сами по себе — только здесь,
    /// с явной причиной. Каждое изменение логируется и показывается игроку (если он участник).
    /// </summary>
    public void ChangeRelation(int from, int to, float delta, string reason)
    {
        if (from == to)
            return;
        WorldData world = DataManager.Instance.World;
        CountryData cf = world.Countries[from];
        CountryData ct = world.Countries[to];
        if (cf == null || ct == null || !cf.IsAlive || !ct.IsAlive)
            return;

        float before = cf.RelationWith(to);
        float after = Mathf.Clamp(before + delta, -100f, 100f);
        cf.Relations[to] = after;

        if (Math.Abs(after - before) < 0.5f)
            return; // нет заметного изменения — не спамим

        string dir = after > before ? "+" : "";
        LogService.Instance.Info($"Relations: {cf.Code} -> {ct.Code} {dir}{after - before:0} ({reason})");

        // Показываем игроку, если он — одна из сторон.
        int playerId = GameManager.Instance.ActiveOptions?.PlayerCountryId ?? -1;
        if (from == playerId || to == playerId)
        {
            string otherName = world.CountryName(from == playerId ? ct : cf, LocalizationManager.Instance.Language);
            EventBus.Instance.EmitUINotification(
                $"{otherName}: {(after > before ? "+" : "")}{after - before:0} {LocalizationManager.Instance.Get("REL")} ({LocalizationManager.Instance.Get(reason)})");
        }
        EventBus.Instance.EmitDiplomacyUpdated();
    }

    public void ImproveRelations(int a, int b, float amount = 5f, string reason = "REL_IMPROVE")
    {
        ChangeRelation(a, b, amount, reason);
    }

    public void Insult(int a, int b)
    {
        ChangeRelation(a, b, -10f, "REL_INSULT");
    }

    public bool FormAlliance(int a, int b)
    {
        if (a == b || AreAtWar(a, b) || GetStatus(a, b) == DiplomacyStatus.Alliance)
            return false;
        WorldData world = DataManager.Instance.World;
        if (world.GetCountry(a) == null || world.GetCountry(b) == null ||
            !world.GetCountry(a).IsAlive || !world.GetCountry(b).IsAlive)
            return false;
        SetStatus(a, b, DiplomacyStatus.Alliance);
        ImproveRelations(a, b, 25f, "REL_ALLIANCE");
        ImproveRelations(b, a, 25f, "REL_ALLIANCE");
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

    /// <summary>Санкции (a вводит/снимает против b) — влияют на торговлю и отношения.</summary>
    public void ToggleSanctions(int a, int b)
    {
        WorldData world = DataManager.Instance.World;
        var set = world.Countries[a].TradePolicy.Sanctioned;
        if (!set.Remove(b))
        {
            set.Add(b);
            ChangeRelation(a, b, -15f, "REL_SANCTION");
        }
        EventBus.Instance.EmitTradeUpdated();
    }

    /// <summary>Внешний заём: страна a берёт в долг у b (реальные деньги + процент).</summary>
    public bool TakeLoan(int borrowerId, int lenderId, double amount)
    {
        WorldData world = DataManager.Instance.World;
        CountryData borrower = world.Countries[borrowerId];
        CountryData lender = world.Countries[lenderId];
        if (borrower == null || lender == null || amount <= 0)
            return false;
        if (lender.Treasury < amount)
            return false;
        if (borrower.RelationWith(lenderId) < -20f)
            return false; // плохие отношения — кредит не дадут

        lender.Treasury -= amount;
        borrower.Treasury += amount;
        // Внешний долг растёт у заёмщика, у кредитора — внешний актив.
        Economy.EconomyManager.Instance.Economy.Countries[borrowerId].ExternalDebt += amount;
        ChangeRelation(borrowerId, lenderId, 5f, "REL_LOAN");
        ChangeRelation(lenderId, borrowerId, 5f, "REL_LOAN");
        LogService.Instance.Info($"Loan: {borrower.Code} borrowed {amount:N0} from {lender.Code}");
        return true;
    }

    // --- Тик -------------------------------------------------------------

    public void Tick()
    {
        // Отношения НЕ дрейфуют сами по себе: они меняются только через ChangeRelation
        // с явной причиной (война/мир/альянс/оскорбление/событие/торговля).

        // Военное истощение растёт у воюющих (это отдельная механика, не отношения).
        WorldData world = DataManager.Instance.World;
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
