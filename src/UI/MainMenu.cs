using Godot;
using GrandStrategy.Core;

namespace GrandStrategy.UI;

/// <summary>
/// Главное меню. Кнопки: Новая игра, Продолжить (если есть сейв), Загрузить,
/// Настройки, О создании, Выход. Строится программно.
/// </summary>
public partial class MainMenu : Control
{
    public override void _Ready()
    {
        AddChild(UiKit.Background());
        VBoxContainer col = UiKit.CenterColumn(this);

        col.AddChild(UiKit.Title(LocalizationManager.Instance.Get("APP_NAME")));

        var version = UiKit.Label($"v{GameConstants.AppVersion}", 13);
        version.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(version);

        var newGame = UiKit.Button(L("MENU_NEW_GAME"), () =>
            GetTree().ChangeSceneToFile("res://scenes/NewGame.tscn"));
        col.AddChild(newGame);
        newGame.Disabled = !DataManager.Instance.IsLoaded;

        bool hasSave = SaveManager.Instance.ListSaves().Length > 0;
        var continueBtn = UiKit.Button(L("MENU_CONTINUE"), () =>
        {
            string slot = SaveManager.Instance.SaveExists("autosave") ? "autosave" : SaveManager.Instance.ListSaves()[0];
            if (GameManager.Instance.LoadGame(slot))
                GetTree().ChangeSceneToFile("res://scenes/Game.tscn");
        });
        continueBtn.Disabled = !hasSave;
        col.AddChild(continueBtn);

        var loadBtn = UiKit.Button(L("MENU_LOAD"), () =>
            GetTree().ChangeSceneToFile("res://scenes/Load.tscn"));
        loadBtn.Disabled = !hasSave;
        col.AddChild(loadBtn);

        col.AddChild(UiKit.Button(L("MENU_SETTINGS"), () =>
            GetTree().ChangeSceneToFile("res://scenes/Settings.tscn")));

        col.AddChild(UiKit.Button(L("MENU_CREDITS"), () =>
            GetTree().ChangeSceneToFile("res://scenes/Credits.tscn")));

        col.AddChild(UiKit.Button(L("MENU_QUIT"), () => GetTree().Quit()));

        if (!DataManager.Instance.IsLoaded)
        {
            var warn = UiKit.Label(L("MENU_NO_DATA"), 13);
            warn.HorizontalAlignment = HorizontalAlignment.Center;
            col.AddChild(warn);
        }
    }

    private static string L(string key) => LocalizationManager.Instance.Get(key);
}
