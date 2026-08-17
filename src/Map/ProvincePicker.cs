using Godot;
using GrandStrategy.Data;

namespace GrandStrategy.Map;

/// <summary>
/// Определение провинции под курсором: O(1) чтением пикселя из кэшированной ID-карты.
/// Никаких геометрических проверок пересечений на кадр.
/// </summary>
public partial class ProvincePicker : Node
{
    private Image _idImage = null!;
    private int _width;
    private int _height;

    public void Initialize(Image idImage)
    {
        _idImage = idImage;
        _width = idImage.GetWidth();
        _height = idImage.GetHeight();
    }

    /// <summary>Мировые координаты -> индекс провинции (-1, если океан/вне карты).</summary>
    public int WorldToProvinceIndex(Vector2 world)
    {
        int x = (int)world.X;
        int y = (int)world.Y;
        if (x < 0 || y < 0 || x >= _width || y >= _height)
            return -1;
        return Decode(_idImage.GetPixel(x, y)) - 1;
    }

    /// <summary>Пиксель текстуры (без проверки границ) -> индекс провинции.</summary>
    public int TexturePixelToProvinceIndex(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _width || y >= _height)
            return -1;
        return Decode(_idImage.GetPixel(x, y)) - 1;
    }

    private static int Decode(Color c)
    {
        int r = (int)(c.R * 255f + 0.5f);
        int g = (int)(c.G * 255f + 0.5f);
        int b = (int)(c.B * 255f + 0.5f);
        return r + g * 256 + b * 65536;
    }
}
