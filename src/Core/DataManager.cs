using System;
using System.Text;
using Godot;
using GrandStrategy.Data;

namespace GrandStrategy.Core;

/// <summary>
/// Синглтон данных. Загружает мировые данные (WorldDataLoader), держит ссылку на WorldData,
/// валидирует целостность и умеет печатать сводную статистику в отладочную консоль.
/// Autoload #5.
/// </summary>
public partial class DataManager : Node
{
    public static DataManager Instance { get; private set; } = null!;

    public WorldData World { get; private set; } = null!;
    public bool IsLoaded { get; private set; }

    /// <summary>Текущие пути к текстурам карты (зависят от сценария).</summary>
    public string IdMapPath = "res://data/cache/id_map.png";
    public string BorderMaskPath = "res://data/cache/border_mask.png";

    /// <summary>Текущий год сценария (2024 или 1936).</summary>
    public int ScenarioYear = 2024;

    public override void _Ready()
    {
        Instance = this;
        IsLoaded = false;
        World = new WorldData();
    }

    /// <summary>Загрузка мировых данных под конкретный сценарий (год).</summary>
    public bool LoadScenario(int year)
    {
        ScenarioYear = year;
        if (year >= 2000)
        {
            IdMapPath = "res://data/cache/id_map.png";
            BorderMaskPath = "res://data/cache/border_mask.png";
            return LoadWorldData(WorldDataLoader.WorldCachePath);
        }
        else
        {
            IdMapPath = "res://data/cache/id_map_1938.png";
            BorderMaskPath = "res://data/cache/border_mask_1938.png";
            return LoadWorldData("res://data/cache/world_1938.json");
        }
    }

    /// <summary>Загрузка мировых данных из кэша + CSV-баланса.</summary>
    public bool LoadWorldData(string cachePath = WorldDataLoader.WorldCachePath)
    {
        try
        {
            World = WorldDataLoader.Load(cachePath);
            IsLoaded = true;
            LogService.Instance.Info($"DataManager: world loaded ({World.ProvinceCount} provinces, {World.CountryCount} countries, {World.GoodCount} goods)");
            EventBus.Instance.EmitDataLoaded();
            return true;
        }
        catch (Exception ex)
        {
            IsLoaded = false;
            LogService.Instance.Error($"DataManager: load failed — {ex.Message}");
            EventBus.Instance.EmitDataValidationFailed(ex.Message);
            return false;
        }
    }

    /// <summary>Валидация загруженных данных; при ошибках — в лог и EventBus.</summary>
    public bool ValidateData()
    {
        if (!IsLoaded)
            return false;

        DataValidator.Result result = DataValidator.Validate(World);
        foreach (string err in result.Errors)
            LogService.Instance.Error($"Data validation error: {err}");
        foreach (string warn in result.Warnings)
            LogService.Instance.Warning($"Data validation warning: {warn}");

        if (!result.Ok)
            EventBus.Instance.EmitDataValidationFailed($"{result.Errors.Count} errors");

        return result.Ok;
    }

    /// <summary>Печать сводной статистики мира в консоль (отладочная команда dump_stats).</summary>
    public string DumpStats()
    {
        if (!IsLoaded)
            return "Data not loaded";

        var sb = new StringBuilder();
        sb.AppendLine($"Provinces: {World.ProvinceCount}");
        sb.AppendLine($"Countries: {World.CountryCount}");
        sb.AppendLine($"Goods: {World.GoodCount}");
        sb.AppendLine($"Currencies: {World.Currencies.Length}");
        sb.AppendLine($"Buildings: {World.Buildings.Length}");
        sb.AppendLine($"Laws: {World.Laws.Length}");

        long totalPop = 0;
        foreach (ProvinceData p in World.Provinces)
            totalPop += p.TotalPopulation;
        sb.AppendLine($"Total population: {totalPop:N0}");

        double totalGdp = 0;
        foreach (CountryData c in World.Countries)
            if (c != null) totalGdp += c.Gdp;
        sb.AppendLine($"Total GDP (est): {totalGdp:N0}");

        GD.Print(sb.ToString());
        return sb.ToString();
    }
}
