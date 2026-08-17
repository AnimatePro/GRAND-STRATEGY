using Godot;

namespace GrandStrategy.Core;

/// <summary>
/// Скрипт boot-сцены. Все автозагружаемые менеджеры уже готовы к моменту _Ready.
/// Порядок инициализации на старте: логи -> настройки -> локализация -> заглушка экрана.
/// В M4 этот экран сменится полноценным boot -> main menu.
/// </summary>
public partial class Main : Node
{
    private Label? _titleLabel;
    private Label? _statusLabel;

    public override void _Ready()
    {
        LogService log = LogService.Instance;
        log.Info($"===== {GameConstants.AppVersion} boot start =====");

        // 1. Настройки (создают файл по умолчанию при первом запуске).
        SettingsManager.Instance.Load();

        // 2. Локализация на языке из настроек.
        LocalizationManager.Instance.LoadLanguage(SettingsManager.Instance.Current.Language);
        log.Info($"Language: {LocalizationManager.Instance.Language}");

        // 3. Обновляем стартовый экран локализованными строками.
        _titleLabel = GetNodeOrNull<Label>("CanvasLayer/BootUI/Center/VBox/Title");
        _statusLabel = GetNodeOrNull<Label>("CanvasLayer/BootUI/Center/VBox/Status");
        UpdateBootScreen();

        log.Info("boot complete — all core singletons ready");

        // 4. Переход в игровую сцену (карта). M4 заменит это на main menu.
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://scenes/Game.tscn");
    }

    private void UpdateBootScreen()
    {
        if (_titleLabel != null)
            _titleLabel.Text = LocalizationManager.Instance.Get("BOOT_TITLE");
        if (_statusLabel != null)
            _statusLabel.Text = LocalizationManager.Instance.Get("BOOT_STATUS_READY")
                + $" | v{GameConstants.AppVersion} | turn {TimeManager.Instance.CurrentTurn}";
    }
}
