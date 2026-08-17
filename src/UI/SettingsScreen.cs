using Godot;
using GrandStrategy.Core;

namespace GrandStrategy.UI;

/// <summary>
/// Экран настроек: язык, масштаб UI, полноэкранный режим, vsync, ограничение FPS,
/// громкость (мастер/интерфейс/эффекты). Применяется сразу, сохраняется в settings.json.
/// </summary>
public partial class SettingsScreen : Control
{
    private OptionButton _language = null!;
    private OptionButton _uiScale = null!;
    private OptionButton _fullscreen = null!;
    private CheckBox _vsync = null!;
    private HSlider _master = null!;
    private HSlider _ui = null!;
    private HSlider _sfx = null!;

    public override void _Ready()
    {
        Settings s = SettingsManager.Instance.Current;

        AddChild(UiKit.Background());
        VBoxContainer col = UiKit.CenterColumn(this, 560f);
        col.AddChild(UiKit.Title(L("SETTINGS_TITLE")));

        // Язык.
        col.AddChild(UiKit.Label(L("SETTINGS_LANGUAGE"), 14));
        _language = UiKit.Dropdown(new[]
        {
            new UiKit.DropdownOption("English", 0),
            new UiKit.DropdownOption("Русский", 1),
        }, s.Language == "ru" ? 1 : 0);
        _language.ItemSelected += OnLanguageChanged;
        col.AddChild(_language);

        // Масштаб UI.
        col.AddChild(UiKit.Label(L("SETTINGS_UI_SCALE"), 14));
        _uiScale = UiKit.Dropdown(new[]
        {
            new UiKit.DropdownOption("75%", 0),
            new UiKit.DropdownOption("100%", 1),
            new UiKit.DropdownOption("125%", 2),
            new UiKit.DropdownOption("150%", 3),
        }, ScaleIndex(s.Display.UiScale));
        _uiScale.ItemSelected += _ => ApplyAndSave();
        col.AddChild(_uiScale);

        // Полноэкранный режим.
        col.AddChild(UiKit.Label(L("SETTINGS_FULLSCREEN"), 14));
        _fullscreen = UiKit.Dropdown(new[]
        {
            new UiKit.DropdownOption(L("SETTINGS_WINDOWED"), 0),
            new UiKit.DropdownOption(L("SETTINGS_BORDERLESS"), 1),
            new UiKit.DropdownOption(L("SETTINGS_OPT_FULLSCREEN"), 2),
        }, s.Display.Fullscreen ? 2 : (s.Display.Borderless ? 1 : 0));
        _fullscreen.ItemSelected += _ => ApplyAndSave();
        col.AddChild(_fullscreen);

        // Vsync.
        _vsync = new CheckBox { Text = L("SETTINGS_VSYNC"), ButtonPressed = s.Display.Vsync };
        _vsync.AddThemeFontSizeOverride("font_size", 15);
        _vsync.Toggled += _ => ApplyAndSave();
        col.AddChild(_vsync);

        // Громкости.
        col.AddChild(UiKit.Label(L("SETTINGS_MASTER_VOLUME"), 14));
        _master = VolumeSlider(s.Audio.MasterVolume);
        _master.ValueChanged += _ => ApplyAndSave();
        col.AddChild(_master);

        col.AddChild(UiKit.Label(L("SETTINGS_UI_VOLUME"), 14));
        _ui = VolumeSlider(s.Audio.UiVolume);
        _ui.ValueChanged += _ => ApplyAndSave();
        col.AddChild(_ui);

        col.AddChild(UiKit.Label(L("SETTINGS_SFX_VOLUME"), 14));
        _sfx = VolumeSlider(s.Audio.SfxVolume);
        _sfx.ValueChanged += _ => ApplyAndSave();
        col.AddChild(_sfx);

        col.AddChild(UiKit.Button(L("MENU_BACK"), () =>
            GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn")));
    }

    private static HSlider VolumeSlider(float value)
    {
        var slider = new HSlider
        {
            MinValue = 0, MaxValue = 1.0, Step = 0.01, Value = value,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        return slider;
    }

    private void OnLanguageChanged(long index)
    {
        string lang = index == 1 ? "ru" : "en";
        SettingsManager.Instance.Current.Language = lang;
        SettingsManager.Instance.Save();
        LocalizationManager.Instance.LoadLanguage(lang);
        EventBus.Instance.EmitSettingsChanged();
        // Перестроить экран на новом языке.
        GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
    }

    private void ApplyAndSave()
    {
        Settings s = SettingsManager.Instance.Current;

        s.Display.UiScale = _uiScale.Selected switch { 0 => 0.75f, 2 => 1.25f, 3 => 1.5f, _ => 1.0f };

        int fs = _fullscreen.Selected;
        s.Display.Fullscreen = fs == 2;
        s.Display.Borderless = fs == 1;

        s.Display.Vsync = _vsync.ButtonPressed;
        s.Audio.MasterVolume = (float)_master.Value;
        s.Audio.UiVolume = (float)_ui.Value;
        s.Audio.SfxVolume = (float)_sfx.Value;

        SettingsManager.Instance.Save();
        Main.ApplyDisplaySettings();
        EventBus.Instance.EmitSettingsChanged();
    }

    private static int ScaleIndex(float scale) =>
        scale <= 0.75f ? 0 : scale >= 1.5f ? 3 : scale >= 1.25f ? 2 : 1;

    private static string L(string key) => LocalizationManager.Instance.Get(key);
}
