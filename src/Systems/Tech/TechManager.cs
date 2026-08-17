using System.Collections.Generic;
using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;
using GrandStrategy.Systems.Economy;

namespace GrandStrategy.Systems.Tech;

/// <summary>
/// Менеджер технологий (Autoload #13). Исследования финансируются из бюджета
/// (доля Spending.Research от доходов). Прогресс накапливается каждый ход; по завершении
/// эффект суммируется в TechEffects страны. Autoload #13.
/// </summary>
public partial class TechManager : Node
{
    public static TechManager Instance { get; private set; } = null!;

    public TechData[] Techs { get; private set; } = System.Array.Empty<TechData>();

    private readonly Dictionary<int, double> _progress = new();
    private readonly Dictionary<int, HashSet<int>> _researched = new();

    public override void _Ready()
    {
        Instance = this;
        LoadTechs();
    }

    private void LoadTechs()
    {
        var list = new List<TechData>();
        foreach (Dictionary<string, string> row in CsvTableLoader.Load("res://data/techs.csv"))
        {
            list.Add(new TechData
            {
                Id = list.Count, // индекс в массиве (0-based)
                NameKey = CsvTableLoader.Str(row, "name_key"),
                Category = (TechCategory)CsvTableLoader.Int(row, "category"),
                Cost = CsvTableLoader.Double(row, "cost", 100),
                FoodMult = CsvTableLoader.Double(row, "food_mult"),
                ProductionMult = CsvTableLoader.Double(row, "production_mult"),
                MilitaryMult = CsvTableLoader.Double(row, "military_mult"),
                TaxMult = CsvTableLoader.Double(row, "tax_mult"),
                ResearchMult = CsvTableLoader.Double(row, "research_mult"),
            });
        }
        Techs = list.ToArray();
        LogService.Instance.Info($"TechManager: {Techs.Length} techs loaded");
    }

    public void Reset()
    {
        _progress.Clear();
        _researched.Clear();
    }

    public bool IsResearched(int countryId, int techId) =>
        _researched.TryGetValue(countryId, out HashSet<int>? set) && set.Contains(techId);

    public double Progress(int countryId) =>
        _progress.TryGetValue(countryId, out double p) ? p : 0.0;

    public IReadOnlyCollection<int> Researched(int countryId) =>
        _researched.TryGetValue(countryId, out HashSet<int>? set) ? set : System.Array.Empty<int>();

    /// <summary>Агрегированные эффекты технологий страны.</summary>
    public TechEffects GetEffects(int countryId)
    {
        var fx = new TechEffects();
        if (_researched.TryGetValue(countryId, out HashSet<int>? set))
        {
            foreach (int tid in set)
            {
                TechData t = Techs[tid];
                fx += TechEffects.From(t);
            }
        }
        // 1.0 = нет эффекта; далее множители (1 + fx.X).
        return fx;
    }

    public void Tick()
    {
        WorldData world = DataManager.Instance.World;
        WorldEconomy economy = EconomyManager.Instance.Economy;
        if (economy.Countries.Length != world.CountryCount)
            return;

        for (int c = 0; c < world.CountryCount; c++)
        {
            if (!world.Countries[c].IsAlive)
                continue;

            TechEffects fx = GetEffects(c);
            CountryEconomy eco = economy.Countries[c];
            double budget = eco.BudgetRevenue * eco.Spending.Research;
            double speed = 1.0 + fx.Research;
            budget *= speed;

            if (!_researched.TryGetValue(c, out HashSet<int>? set))
                _researched[c] = set = new HashSet<int>();

            // Цель — первая неисследованная технология.
            TechData target = default;
            bool hasTarget = false;
            for (int i = 0; i < Techs.Length; i++)
            {
                if (!set.Contains(i))
                {
                    target = Techs[i];
                    hasTarget = true;
                    break;
                }
            }
            if (!hasTarget)
                continue;

            double progress = Progress(c) + budget;
            while (hasTarget && progress >= target.Cost)
            {
                progress -= target.Cost;
                set.Add(target.Id);
                LogService.Instance.Info($"Tech: country {c} researched '{target.NameKey}'");
                // Следующая цель.
                hasTarget = false;
                for (int i = 0; i < Techs.Length; i++)
                {
                    if (!set.Contains(i))
                    {
                        target = Techs[i];
                        hasTarget = true;
                        break;
                    }
                }
            }
            _progress[c] = progress;
        }
    }

    // --- Сохранение/восстановление -------------------------------------------

    public Dictionary<int, List<int>> GetResearchedForSave()
    {
        var result = new Dictionary<int, List<int>>();
        foreach (KeyValuePair<int, HashSet<int>> kv in _researched)
            result[kv.Key] = new List<int>(kv.Value);
        return result;
    }

    public Dictionary<int, double> GetProgressForSave() => new(_progress);

    public void RestoreResearched(Dictionary<int, List<int>>? researched)
    {
        _researched.Clear();
        if (researched == null)
            return;
        foreach (KeyValuePair<int, List<int>> kv in researched)
            _researched[kv.Key] = new HashSet<int>(kv.Value);
    }

    public void RestoreProgress(Dictionary<int, double>? progress)
    {
        _progress.Clear();
        if (progress == null)
            return;
        foreach (KeyValuePair<int, double> kv in progress)
            _progress[kv.Key] = kv.Value;
    }

    /// <summary>Множитель производства для страны (1 + эффекты промышленности).</summary>
    public double ProductionMult(int countryId) => 1.0 + GetEffects(countryId).Production;

    public double FoodMult(int countryId) => 1.0 + GetEffects(countryId).Food;

    public double MilitaryMult(int countryId) => 1.0 + GetEffects(countryId).Military;

    public double TaxMult(int countryId) => 1.0 + GetEffects(countryId).Tax;
}
