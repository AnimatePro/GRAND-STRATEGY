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

    public override void _Ready()
    {
        Instance = this;
        IsLoaded = false;
        World = new WorldData();
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
