using Godot;

namespace GrandStrategy.UI;

/// <summary>
/// Единая тёмная тема интерфейса (панели, кнопки, поля, метки) — применяется глобально
/// через ThemeDB.FallbackTheme на boot. Визуально близко к AoH3: тёмные панели с тонкой
/// рамкой, акцентные кнопки, сдержанная палитра.
/// </summary>
public static class GameTheme
{
    public static readonly Color Accent = new(0.30f, 0.52f, 0.82f);
    public static readonly Color AccentHover = new(0.40f, 0.63f, 0.92f);
    public static readonly Color Text = new(0.86f, 0.89f, 0.94f);
    public static readonly Color TextDim = new(0.62f, 0.66f, 0.74f);

    public static Theme Build()
    {
        var theme = new Theme();

        var panelStyle = Style(new Color(0.13f, 0.16f, 0.22f, 0.97f), new Color(0.24f, 0.30f, 0.42f));
        theme.SetStylebox("panel", "Panel", panelStyle);
        theme.SetStylebox("panel", "PanelContainer", panelStyle);
        theme.SetStylebox("panel", "ScrollContainer",
            Style(new Color(0.11f, 0.14f, 0.19f, 0.92f), new Color(0.18f, 0.22f, 0.30f)));

        theme.SetStylebox("normal", "Button",
            Style(new Color(0.17f, 0.21f, 0.29f), new Color(0.26f, 0.32f, 0.44f)));
        theme.SetStylebox("hover", "Button",
            Style(new Color(0.22f, 0.27f, 0.37f), new Color(0.38f, 0.47f, 0.62f)));
        theme.SetStylebox("pressed", "Button",
            Style(new Color(0.12f, 0.15f, 0.21f), Accent));
        theme.SetStylebox("disabled", "Button",
            Style(new Color(0.13f, 0.15f, 0.20f), new Color(0.18f, 0.21f, 0.27f)));
        theme.SetColor("font_color", "Button", Text);
        theme.SetColor("font_disabled_color", "Button", TextDim);
        theme.SetFontSize("font_size", "Button", 15);

        theme.SetColor("font_color", "Label", Text);
        theme.SetFontSize("font_size", "Label", 15);

        theme.SetStylebox("normal", "LineEdit",
            Style(new Color(0.11f, 0.14f, 0.19f), new Color(0.26f, 0.32f, 0.44f)));
        theme.SetStylebox("focus", "LineEdit",
            Style(new Color(0.11f, 0.14f, 0.19f), Accent));
        theme.SetColor("font_color", "LineEdit", Text);
        theme.SetFontSize("font_size", "LineEdit", 15);

        theme.SetColor("font_color", "CheckBox", Text);
        theme.SetFontSize("font_size", "CheckBox", 15);

        theme.SetColor("font_color", "OptionButton", Text);
        theme.SetFontSize("font_size", "OptionButton", 15);

        theme.SetConstant("separation", "VBoxContainer", 8);
        theme.SetConstant("separation", "HBoxContainer", 10);

        return theme;
    }

    /// <summary>Скруглённая тёмная панель с тонкой рамкой.</summary>
    public static StyleBoxFlat Style(Color bg, Color border)
    {
        return new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 6, ContentMarginBottom = 6,
        };
    }
}
