using Godot;
using GrandStrategy.Core;

namespace GrandStrategy.UI;

/// <summary>Экран «О создании» — краткая информация и кнопка назад.</summary>
public partial class CreditsScreen : Control
{
    public override void _Ready()
    {
        AddChild(UiKit.Background());
        VBoxContainer col = UiKit.CenterColumn(this, 560f);
        col.AddChild(UiKit.Title(L("CREDITS_TITLE")));

        col.AddChild(UiKit.Label(LocalizationManager.Instance.Get("APP_NAME"), 22));
        col.AddChild(UiKit.Label($"v{GameConstants.AppVersion}", 15));
        col.AddChild(UiKit.Label(L("CREDITS_ENGINE"), 15));
        col.AddChild(UiKit.Label("Godot 4.7.1 · C# (.NET 8)", 15));
        col.AddChild(UiKit.Label(L("CREDITS_DESC"), 15));

        col.AddChild(UiKit.Button(L("MENU_BACK"), () =>
            GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn")));
    }

    private static string L(string key) => LocalizationManager.Instance.Get(key);
}
