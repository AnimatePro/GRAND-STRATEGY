using Godot;

namespace GrandStrategy.Core;

/// <summary>
/// Скрипт boot-сцены. Инициализирует настройки, локализацию, загружает мировые данные
/// (для меню — список стран) и переходит в главное меню. Меню и игровая сцена — отдельные
/// сцены; автозагружаемые менеджеры живут в дереве между сменами сцен.
/// </summary>
public partial class Main : Node
{
    public override void _Ready()
    {
        LogService log = LogService.Instance;
        log.Info($"===== {GameConstants.AppVersion} boot start =====");

        // Системный шрифт с поддержкой кириллицы (fallback-цепочка).
        var sysFont = new SystemFont
        {
            FontNames = new[] { "DejaVu Sans", "Noto Sans", "Segoe UI", "Arial", "Liberation Sans" },
        };
        ThemeDB.FallbackFont = sysFont;
        // Глобальная тёмная тема задаётся ресурсом assets/ui/theme.tres через
        // настройку проекта gui/theme/custom (см. project.godot).

        // 1. Настройки.
        SettingsManager.Instance.Load();
        ApplyDisplaySettings();

        // 2. Локализация.
        LocalizationManager.Instance.LoadLanguage(SettingsManager.Instance.Current.Language);
        log.Info($"Language: {LocalizationManager.Instance.Language}");

        // 3. Мировые данные (если кэш есть — меню покажет страны).
        if (DataManager.Instance.LoadWorldData())
            DataManager.Instance.ValidateData();
        else
            log.Warning("World data not available — New Game disabled (run MapImporterTool)");

        log.Info("boot complete — entering main menu");

        // 4. Главное меню.
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://scenes/MainMenu.tscn");
    }

    /// <summary>Применение настроек дисплея к окну.</summary>
    public static void ApplyDisplaySettings()
    {
        DisplaySettings d = SettingsManager.Instance.Current.Display;
        DisplayServer.WindowSetVsyncMode(d.Vsync
            ? DisplayServer.VSyncMode.Enabled
            : DisplayServer.VSyncMode.Disabled);

        if (d.Fullscreen)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        }
        else if (d.Borderless)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
        }
        else
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
        }

        if (d.ResolutionWidth > 0 && d.ResolutionHeight > 0 && !d.Fullscreen)
            DisplayServer.WindowSetSize(new Vector2I(d.ResolutionWidth, d.ResolutionHeight));
    }
}
