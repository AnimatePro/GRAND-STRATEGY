using System;
using System.Collections.Generic;
using Godot;
using GrandStrategy.Utils;

namespace GrandStrategy.Core;

/// <summary>
/// Синглтон локализации. Словари лежат в res://localization/{lang}.csv (формат: key,value).
/// Никаких захардкоженных UI-строк — интерфейс обращается только через Get(key, args).
/// Поддержка подстановки {0},{1},... и fallback на ключ при отсутствии перевода.
/// Autoload #4.
/// </summary>
public partial class LocalizationManager : Node
{
    public static LocalizationManager Instance { get; private set; } = null!;

    private readonly Dictionary<string, string> _strings = new();
    private string _language = GameConstants.DefaultLanguage;

    public string Language => _language;

    public override void _Ready() => Instance = this;

    /// <summary>Загружает указанный язык; при ошибке откатывается на английский.</summary>
    public void LoadLanguage(string language)
    {
        _language = language;
        _strings.Clear();

        string path = $"res://localization/{language}.csv";
        if (!FileAccess.FileExists(path))
        {
            LogService.Instance.Warning($"Localization: missing {language}.csv, falling back to {GameConstants.DefaultLanguage}");
            _language = GameConstants.DefaultLanguage;
            path = $"res://localization/{GameConstants.DefaultLanguage}.csv";
        }

        if (!FileAccess.FileExists(path))
        {
            LogService.Instance.Error("Localization: default language file missing");
            EventBus.Instance.EmitLocalizationChanged();
            return;
        }

        try
        {
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            string content = file.GetAsText();
            foreach (string[] cells in CsvParser.Parse(content))
            {
                if (cells.Length >= 2 && !string.IsNullOrWhiteSpace(cells[0]))
                    _strings[cells[0].Trim()] = cells[1];
            }
            LogService.Instance.Debug($"Localization loaded: {_language} ({_strings.Count} keys)");
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"Localization parse failed: {ex.Message}");
        }

        EventBus.Instance.EmitLocalizationChanged();
    }

    /// <summary>Возвращает строку по ключу с опциональной подстановкой {0}..{n}.</summary>
    public string Get(string key, params object[] args)
    {
        if (_strings.TryGetValue(key, out string? value))
        {
            if (value == null)
                return key;
            return args.Length == 0 ? value : string.Format(value, args);
        }
        return key; // fallback: сам ключ (гарантирует отсутствие пустых мест в UI)
    }

    public bool Has(string key) => _strings.ContainsKey(key);
}
