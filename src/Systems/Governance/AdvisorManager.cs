using System.Collections.Generic;
using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;
using GrandStrategy.Utils;

namespace GrandStrategy.Systems.Governance;

/// <summary>
/// Советники и очки наследия. Найм министров (экономика/армия/дипломатия) за деньги,
/// бонусы в сфере; очки наследия начисляются за войны/аннексии и тратятся на идеи.
/// </summary>
public partial class AdvisorManager : Node
{
    public static AdvisorManager Instance { get; private set; } = null!;

    private static readonly string[] AdvisorNames =
    {
        "A. Smith", "J. Keynes", "M. Friedman", "C. Clausewitz", "H. Mahan",
        "K. Metternich", "O. von Bismarck", "T. Roosevelt", "D. Ricardo", "A. Hamilton",
    };

    private int _nextAdvisorId = 1;
    private long _seed;

    public override void _Ready()
    {
        Instance = this;
        EventBus.Instance.GameStarted += OnGameStarted;
    }

    private void OnGameStarted() => _nextAdvisorId = 1;

    public void SetSeed(long seed) => _seed = seed;

    /// <summary>Найм советника заданной сферы.</summary>
    public bool HireAdvisor(int ownerId, AdvisorDomain domain)
    {
        CountryData c = DataManager.Instance.World.GetCountry(ownerId);
        if (c == null)
            return false;
        double cost = 800 + c.Advisors.Count * 400;
        if (c.Treasury < cost)
            return false;

        // Лимит: один советник на сферу.
        if (c.Advisors.Exists(a => a.Domain == domain))
            return false;

        c.Treasury -= cost;
        var rng = new Rng(_seed + _nextAdvisorId * 104729L + TimeManager.Instance.CurrentTurn);
        c.Advisors.Add(new AdvisorData
        {
            Id = _nextAdvisorId++,
            OwnerId = ownerId,
            Name = AdvisorNames[rng.NextInt(0, AdvisorNames.Length - 1)],
            Domain = domain,
            Skill = rng.NextInt(1, 5),
        });
        return true;
    }

    /// <summary>Бонус советника в сфере (1.0 без советника).</summary>
    public double AdvisorMult(int ownerId, AdvisorDomain domain)
    {
        CountryData c = DataManager.Instance.World.GetCountry(ownerId);
        if (c == null)
            return 1.0;
        AdvisorData? a = c.Advisors.Find(x => x.Domain == domain);
        return a != null ? 1.0 + a.Skill * 0.04 : 1.0;
    }

    /// <summary>Начисление очков наследия за значимые события.</summary>
    public void AwardLegacy(int ownerId, double amount, string reason)
    {
        CountryData c = DataManager.Instance.World.GetCountry(ownerId);
        if (c == null)
            return;
        c.LegacyPoints += amount;
        LogService.Instance.Info($"Legacy: {c.Code} +{amount:0} ({reason})");
    }
}
