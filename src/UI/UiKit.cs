using System;
using Godot;
using GrandStrategy.Core;

namespace GrandStrategy.UI;

/// <summary>
/// Набор хелперов построения тёмного UI (кнопки, панели, заголовки, выпадающие списки).
/// Единый стиль для меню и HUD. Кнопки автоматически озвучиваются кликом.
/// </summary>
public static class UiKit
{
    public static readonly Color BgColor = new(0.09f, 0.11f, 0.16f, 0.95f);
    public static readonly Color Accent = new(0.23f, 0.43f, 0.71f);
    public static readonly Color TextColor = new(0.88f, 0.9f, 0.94f);

    public static Button Button(string text, Action onPressed)
    {
        var btn = new Button { Text = text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        btn.AddThemeFontSizeOverride("font_size", 16);
        btn.Pressed += () =>
        {
            AudioManager.Instance.PlayClick();
            onPressed();
        };
        return btn;
    }

    public static Label Label(string text, int fontSize = 15)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    public static Label Title(string text)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 40);
        return label;
    }

    public static PanelContainer Panel()
    {
        var panel = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = BgColor,
            BorderColor = Accent,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 14, ContentMarginRight = 14,
            ContentMarginTop = 10, ContentMarginBottom = 10,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    /// <summary>Опция для выпадающего списка.</summary>
    public sealed class DropdownOption
    {
        public string Label;
        public int Value;
        public DropdownOption(string label, int value) { Label = label; Value = value; }
    }

    /// <summary>Строит выпадающий список (OptionButton) с заданными опциями.</summary>
    public static OptionButton Dropdown(DropdownOption[] options, int selectedIndex = 0)
    {
        var dropdown = new OptionButton();
        dropdown.AddThemeFontSizeOverride("font_size", 16);
        foreach (DropdownOption o in options)
            dropdown.AddItem(o.Label, o.Value);
        dropdown.Select(Math.Clamp(selectedIndex, 0, Math.Max(options.Length - 1, 0)));
        return dropdown;
    }

    /// <summary>Полноэкранный тёмный фон (корень экрана).</summary>
    public static ColorRect Background()
    {
        var bg = new ColorRect
        {
            Color = new Color(0.07f, 0.09f, 0.13f, 1f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return bg;
    }

    /// <summary>Центрированный контейнер с колонкой.</summary>
    public static VBoxContainer CenterColumn(Control root, float maxWidth = 560f)
    {
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(center);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        vbox.CustomMinimumSize = new Vector2(maxWidth, 0);
        center.AddChild(vbox);
        return vbox;
    }
}
