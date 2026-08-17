using System;
using System.IO;
using System.Text.Json;
using Godot;

namespace GrandStrategy.Core;

// --- Settings DTOs (immutable at load, mutable at runtime via Apply) ---------

public sealed class Settings
{
    public string Language { get; set; } = GameConstants.DefaultLanguage;
    public DisplaySettings Display { get; set; } = new();
    public GraphicsSettings Graphics { get; set; } = new();
    public AudioSettings Audio { get; set; } = new();
    public GameSettings Game { get; set; } = new();
}

public sealed class DisplaySettings
{
    public int ResolutionWidth { get; set; } = 1920;
    public int ResolutionHeight { get; set; } = 1080;
    public bool Fullscreen { get; set; } = false;
    public bool Borderless { get; set; } = true;
    public bool Vsync { get; set; } = true;
    public float UiScale { get; set; } = 1.0f;
    public int FpsLimit { get; set; } = 0; // 0 = без ограничения
}

public sealed class GraphicsSettings
{
    public int MapQuality { get; set; } = 2;       // 0 low .. 2 high
    public bool ShowLabels { get; set; } = true;
    public bool ColorblindMode { get; set; } = false;
    public bool PerformanceMode { get; set; } = false;
}

public sealed class AudioSettings
{
    public float MasterVolume { get; set; } = 1.0f;
    public float MusicVolume { get; set; } = 1.0f;
    public float SfxVolume { get; set; } = 1.0f;
    public float UiVolume { get; set; } = 1.0f;
}

public sealed class GameSettings
{
    public bool Autosave { get; set; } = true;
    public bool PauseOnEvents { get; set; } = true;
    public bool EdgePan { get; set; } = true;
    public float CameraSpeed { get; set; } = 1.0f;
}

/// <summary>
/// Синглтон настроек. Хранит настройки в user://settings.json (System.Text.Json),
/// атомарная запись через temp+rename. Изменение настроек рассылает SettingsChanged.
/// Autoload #3.
/// </summary>
public partial class SettingsManager : Node
{
    public static SettingsManager Instance { get; private set; } = null!;

    private const string SettingsPath = "user://settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public Settings Current { get; private set; } = new();

    public override void _Ready() => Instance = this;

    public void Load()
    {
        string path = ProjectSettings.GlobalizePath(SettingsPath);
        if (!File.Exists(path))
        {
            LogService.Instance.Debug("Settings: no file found, saving defaults");
            Save();
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            Settings? loaded = JsonSerializer.Deserialize<Settings>(json, JsonOptions);
            if (loaded != null)
            {
                Current = loaded;
                LogService.Instance.Debug("Settings loaded");
            }
            else
            {
                LogService.Instance.Warning("Settings: deserialized to null, using defaults");
                Current = new Settings();
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"Settings load failed: {ex.Message}");
            Current = new Settings();
            Save();
        }
    }

    public void Save()
    {
        try
        {
            string path = ProjectSettings.GlobalizePath(SettingsPath);
            string json = JsonSerializer.Serialize(Current, JsonOptions);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"Settings save failed: {ex.Message}");
        }
    }

    public void Apply(Settings settings)
    {
        Current = settings;
        Save();
        EventBus.Instance.EmitSettingsChanged();
    }
}
