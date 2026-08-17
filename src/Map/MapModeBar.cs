using Godot;

namespace GrandStrategy.Map;

/// <summary>
/// Нижняя панель быстрого переключения режимов карты (как в AoH3).
/// Кнопки режимов + подсказка горячих клавиш. Клик вызывает MapModeController.SetMode.
/// </summary>
public partial class MapModeBar : Control
{
    private static readonly string[] Labels =
    {
        "Political", "Terrain", "Population", "Economy",
        "Trade", "Resources", "Development", "Infrastructure",
        "Diplomacy", "War", "Unrest", "Religion", "Culture",
    };

    private MapModeController? _controller;

    public void Initialize(MapModeController controller)
    {
        _controller = controller;
        MouseFilter = MouseFilterEnum.Ignore;

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(LayoutPreset.BottomWide);
        panel.OffsetTop = -44;
        AddChild(panel);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        panel.AddChild(scroll);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(hbox);

        var group = new ButtonGroup();
        for (int i = 0; i < MapModeController.Order.Length; i++)
        {
            int idx = i;
            var btn = new Button
            {
                Text = Labels[idx],
                ToggleMode = true,
                ButtonGroup = group,
            };
            if (idx == 0)
                btn.ButtonPressed = true;
            btn.Pressed += () => _controller?.SetMode(MapModeController.Order[idx]);
            hbox.AddChild(btn);
        }
    }
}
