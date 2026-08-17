using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;
using GrandStrategy.Utils;
using FileAccess = Godot.FileAccess; // implicit System.IO конфликтует с Godot.FileAccess

namespace GrandStrategy.Systems.Events;

/// <summary>Эффект события (type + значение + цель).</summary>
public sealed class Effect
{
    public string Type { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Target { get; set; } = string.Empty;
}

/// <summary>Вариант выбора в событии.</summary>
public sealed class EventOption
{
    public string TextKey { get; set; } = string.Empty;
    public List<Effect> Effects { get; set; } = new();
}

/// <summary>Событие (data-driven, JSON).</summary>
public sealed class GameEvent
{
    public string Id { get; set; } = string.Empty;
    public string TitleKey { get; set; } = string.Empty;
    public string DescKey { get; set; } = string.Empty;
    public string Scope { get; set; } = "country"; // global/country/province
    public double Mtth { get; set; } = 100;        // среднее время срабатывания (ходы)
    public double Weight { get; set; } = 1.0;
    public bool Once { get; set; }
    public int Cooldown { get; set; }
    public List<string> Conditions { get; set; } = new();
    public List<EventOption> Options { get; set; } = new();
}

/// <summary>
/// Менеджер событий (Autoload #13). Загружает события из data/events.json,
/// каждый ход бросает срабатывания (MTTH) по странам, применяет эффекты выбранного
/// варианта (автовыбор для AI/скорости). Эффекты: деньги, стабильность, отношения,
/// население, военное истощение, модификаторы.
/// </summary>
public partial class EventManager : Node
{
    public static EventManager Instance { get; private set; } = null!;

    private List<GameEvent> _events = new();
    private readonly Dictionary<string, int> _lastFireTurn = new();
    private readonly HashSet<string> _firedOnce = new();
    private long _seed;

    public override void _Ready()
    {
        Instance = this;
        LoadEvents("res://data/events.json");
    }

    public void SetSeed(long seed) => _seed = seed;

    private void LoadEvents(string path)
    {
        if (!FileAccess.FileExists(path))
        {
            LogService.Instance.Warning("EventManager: events.json not found — no events loaded");
            return;
        }
        try
        {
            using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            _events = JsonSerializer.Deserialize<List<GameEvent>>(file.GetAsText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<GameEvent>();
            LogService.Instance.Info($"EventManager: {_events.Count} events loaded");
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"EventManager: events.json parse failed — {ex.Message}");
        }
    }

    public void Tick()
    {
        WorldData world = DataManager.Instance.World;
        var rng = new Rng(_seed + TimeManager.Instance.CurrentTurn);

        foreach (GameEvent ev in _events)
        {
            if (ev.Once && _firedOnce.Contains(ev.Id))
                continue;
            if (_lastFireTurn.TryGetValue(ev.Id, out int last) &&
                TimeManager.Instance.CurrentTurn - last < ev.Cooldown)
                continue;

            for (int c = 0; c < world.CountryCount; c++)
            {
                if (!world.Countries[c].IsAlive)
                    continue;
                if (!ConditionsMet(ev, world.Countries[c]))
                    continue;

                double p = ev.Mtth > 0 ? 1.0 / ev.Mtth : 1.0;
                if (!rng.Chance(p * ev.Weight))
                    continue;

                Fire(ev, c);
                _lastFireTurn[ev.Id] = TimeManager.Instance.CurrentTurn;
                if (ev.Once)
                    _firedOnce.Add(ev.Id);
                break; // одно событие за тик (упрощённо)
            }
        }
    }

    private void Fire(GameEvent ev, int countryId)
    {
        WorldData world = DataManager.Instance.World;
        EventOption? chosen = ev.Options.Count > 0 ? ev.Options[0] : null;
        if (chosen == null)
            return;

        foreach (Effect fx in chosen.Effects)
            ApplyEffect(world, countryId, fx);

        string title = LocalizationManager.Instance.Get(ev.TitleKey);
        LogService.Instance.Info($"Event '{ev.Id}' fired for country {countryId}");
        EventBus.Instance.EmitUINotification(title);
    }

    private void ApplyEffect(WorldData world, int countryId, Effect fx)
    {
        CountryData country = world.Countries[countryId];
        switch (fx.Type)
        {
            case "add_money":
                country.Treasury = Math.Max(country.Treasury + fx.Value, 0.0);
                break;
            case "add_stability":
                country.Stability = Mathf.Clamp(country.Stability + (float)fx.Value, 0f, 100f);
                break;
            case "add_legitimacy":
                country.Legitimacy = Mathf.Clamp(country.Legitimacy + (float)fx.Value, 0f, 100f);
                break;
            case "add_war_exhaustion":
                country.WarExhaustion = Mathf.Clamp(country.WarExhaustion + (float)fx.Value, 0f, 100f);
                break;
            case "change_relations":
                if (int.TryParse(fx.Target, out int other))
                    country.SetRelation(other, country.RelationWith(other) + (float)fx.Value);
                break;
            case "add_modifier":
                var mod = new Modifier
                {
                    Id = fx.Target,
                    Duration = (int)fx.Value,
                    Source = "event",
                };
                mod.Effects["stability"] = 5.0;
                country.Modifiers.Add(mod);
                break;
            case "change_population":
                foreach (int pid in country.OwnedProvinceIds)
                {
                    ProvinceData p = world.GetProvince(pid);
                    double mult = 1.0 + fx.Value;
                    p.MaleAdults = (int)(p.MaleAdults * mult);
                    p.FemaleAdults = (int)(p.FemaleAdults * mult);
                    world.SetProvince(pid, in p);
                }
                break;
        }
    }

    private static bool ConditionsMet(GameEvent ev, CountryData country)
    {
        foreach (string cond in ev.Conditions)
        {
            // Упрощённый язык условий: "stat <op> value" (stability, gdp, treasury, ...).
            string[] parts = cond.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
                continue;
            double value = parts[0] switch
            {
                "stability" => country.Stability,
                "legitimacy" => country.Legitimacy,
                "gdp" => country.Gdp,
                "treasury" => country.Treasury,
                "debt" => country.Debt,
                "war_exhaustion" => country.WarExhaustion,
                "inflation" => country.Inflation,
                _ => double.NaN,
            };
            if (double.IsNaN(value))
                return false;
            if (!double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double threshold))
                return false;

            bool ok = parts[1] switch
            {
                "<" => value < threshold,
                ">" => value > threshold,
                "<=" => value <= threshold,
                ">=" => value >= threshold,
                "=" => Math.Abs(value - threshold) < 1e-9,
                _ => false,
            };
            if (!ok)
                return false;
        }
        return true;
    }
}
