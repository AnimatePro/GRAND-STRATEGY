using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;

namespace GrandStrategy.Systems.Decisions;

/// <summary>
/// Формируемые нации (Autoload #14). Страна может «сформировать» более крупное
/// образование, если контролирует столицы требуемых стран: они аннексируются,
/// страна переименовывается и меняет цвет.
/// </summary>
public partial class FormableManager : Node
{
    public static FormableManager Instance { get; private set; } = null!;

    public List<FormableData> Formables { get; private set; } = new();

    public override void _Ready()
    {
        Instance = this;
        LoadFormables("res://data/formables.json");
    }

    private void LoadFormables(string path)
    {
        if (!FileAccess.FileExists(path))
        {
            LogService.Instance.Warning("FormableManager: formables.json not found");
            return;
        }
        try
        {
            using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            Formables = JsonSerializer.Deserialize<List<FormableData>>(file.GetAsText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<FormableData>();
            LogService.Instance.Info($"FormableManager: {Formables.Count} formables loaded");
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"FormableManager: parse failed — {ex.Message}");
        }
    }

    /// <summary>Список формируемых наций, доступных стране сейчас.</summary>
    public List<FormableData> AvailableFor(int countryId)
    {
        var result = new List<FormableData>();
        foreach (FormableData f in Formables)
            if (CanForm(countryId, f))
                result.Add(f);
        return result;
    }

    public bool CanForm(int countryId, FormableData f)
    {
        WorldData world = DataManager.Instance.World;
        foreach (string code in f.RequiresCodes)
        {
            int targetId = world.CountryByCode(code);
            if (targetId < 0)
                continue; // кода нет в данных — пропускаем требование
            CountryData target = world.Countries[targetId];
            if (target.IsAlive && target.CapitalProvinceId >= 0)
            {
                ProvinceData cap = world.GetProvince(target.CapitalProvinceId);
                // Контролируем = владеем или оккупируем.
                if (cap.OwnerId != countryId && cap.ControllerId != countryId)
                    return false;
            }
        }
        return true;
    }

    /// <summary>Формирует нацию: аннексирует требуемые страны, переименовывает и красит.</summary>
    public bool Form(int countryId, FormableData f)
    {
        WorldData world = DataManager.Instance.World;
        if (!CanForm(countryId, f))
            return false;

        CountryData country = world.Countries[countryId];
        country.NameKey = f.NameKey;
        try { country.Color = new Color(f.ColorHex); } catch { }

        foreach (string code in f.RequiresCodes)
        {
            int targetId = world.CountryByCode(code);
            if (targetId < 0 || targetId == countryId)
                continue;
            CountryData target = world.Countries[targetId];
            if (!target.IsAlive)
                continue;

            // Аннексия: передаём все провинции.
            foreach (int pid in target.OwnedProvinceIds)
            {
                ProvinceData p = world.GetProvince(pid);
                p.OwnerId = countryId;
                p.ControllerId = -1;
                world.SetProvince(pid, in p);
                country.OwnedProvinceIds.Add(pid);
            }
            target.IsAlive = false;
            target.OwnedProvinceIds.Clear();
        }

        LogService.Instance.Info($"FormableManager: country {countryId} formed '{f.Id}'");
        EventBus.Instance.EmitUINotification(LocalizationManager.Instance.Get(f.NameKey));
        return true;
    }
}
