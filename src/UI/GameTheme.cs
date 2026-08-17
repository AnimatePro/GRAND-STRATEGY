using Godot;

namespace GrandStrategy.UI;

/// <summary>
/// Программные стили-хелперы поверх глобальной темы assets/ui/theme.tres
/// (задаётся настройкой gui/theme/custom). Визуально близко к AoH3: тёмные панели
/// с золотистой рамкой, акцентные кнопки, сдержанная палитра.
/// </summary>
public static class GameTheme
{
    public static readonly Color Accent = new(0.75f, 0.60f, 0.30f);
    public static readonly Color Text = new(0.88f, 0.86f, 0.80f);
    public static readonly Color TextDim = new(0.55f, 0.56f, 0.58f);

    /// <summary>Скруглённая тёмная панель с тонкой золотистой рамкой.</summary>
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
