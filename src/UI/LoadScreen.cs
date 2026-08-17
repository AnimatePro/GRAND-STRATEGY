using Godot;
using GrandStrategy.Core;

namespace GrandStrategy.UI;

/// <summary>
/// Экран загрузки/управления сохранениями: список сейвов, загрузить, удалить, назад.
/// </summary>
public partial class LoadScreen : Control
{
    private VBoxContainer _list = null!;

    public override void _Ready()
    {
        AddChild(UiKit.Background());
        VBoxContainer col = UiKit.CenterColumn(this, 560f);
        col.AddChild(UiKit.Title(L("LOAD_TITLE")));

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 320),
        };
        col.AddChild(scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_list);

        Rebuild();

        col.AddChild(UiKit.Button(L("MENU_BACK"), () =>
            GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn")));
    }

    private void Rebuild()
    {
        foreach (Node child in _list.GetChildren())
            child.QueueFree();

        string[] saves = SaveManager.Instance.ListSaves();
        if (saves.Length == 0)
        {
            _list.AddChild(UiKit.Label(L("LOAD_EMPTY"), 15));
            return;
        }

        foreach (string slot in saves)
        {
            var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            row.AddChild(UiKit.Label(slot, 16));

            var loadBtn = UiKit.Button(L("LOAD_LOAD"), () =>
            {
                if (GameManager.Instance.LoadGame(slot))
                    GetTree().ChangeSceneToFile("res://scenes/Game.tscn");
            });
            loadBtn.CustomMinimumSize = new Vector2(120, 0);
            row.AddChild(loadBtn);

            var delBtn = UiKit.Button(L("LOAD_DELETE"), () =>
            {
                SaveManager.Instance.DeleteSave(slot);
                Rebuild();
            });
            delBtn.CustomMinimumSize = new Vector2(90, 0);
            row.AddChild(delBtn);

            _list.AddChild(row);
        }
    }

    private static string L(string key) => LocalizationManager.Instance.Get(key);
}
