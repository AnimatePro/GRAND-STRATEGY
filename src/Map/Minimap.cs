using Godot;
using GrandStrategy.Data;

namespace GrandStrategy.Map;

/// <summary>
/// Миникарта: уменьшенная копия карты (строится из ID-карты + цветов провинций)
/// с прямоугольником текущего обзора. Клик по миникарте центрирует камеру.
/// </summary>
public partial class Minimap : Control
{
    public const int Downscale = 16; // мини-пиксель = 16 мировых пикселей

    private MapRenderer? _renderer;
    private CameraRig? _camera;
    private ImageTexture _texture = null!;
    private Vector2 _mapSize;
    private float _scaleX;
    private float _scaleY;

    public void Initialize(MapRenderer renderer, CameraRig camera)
    {
        _renderer = renderer;
        _camera = camera;
        _mapSize = renderer.WorldSize;
        Rebuild();
    }

    /// <summary>Перестраивает миникарту (вызывается при смене режима/цветов).</summary>
    public void Rebuild()
    {
        if (_renderer == null)
            return;

        int w = Mathf.Max(1, (int)(_renderer.WorldSize.X / Downscale));
        int h = Mathf.Max(1, (int)(_renderer.WorldSize.Y / Downscale));
        _scaleX = _renderer.WorldSize.X / w;
        _scaleY = _renderer.WorldSize.Y / h;

        Image img = Image.CreateEmpty(w, h, false, Image.Format.Rgb8);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int worldX = Mathf.Clamp((int)(x * _scaleX), 0, (int)_renderer.WorldSize.X - 1);
                int worldY = Mathf.Clamp((int)(y * _scaleY), 0, (int)_renderer.WorldSize.Y - 1);
                int index = DecodeIndex(_renderer.IdImage.GetPixel(worldX, worldY));
                img.SetPixel(x, y, index >= 0 ? _renderer.ProvinceColor(index) : new Color(0.09f, 0.13f, 0.2f));
            }
        }
        _texture = ImageTexture.CreateFromImage(img);
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            Vector2 local = mb.Position;
            float fx = local.X / Size.X;
            float fy = local.Y / Size.Y;
            Vector2 world = new(fx * _mapSize.X, fy * _mapSize.Y);
            if (_camera != null)
                _camera.Center = world;
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        if (_texture == null || _camera == null)
            return;

        DrawTextureRect(_texture, new Rect2(Vector2.Zero, Size), false);

        // Прямоугольник обзора.
        Vector2 half = GetViewport().GetVisibleRect().Size / (2f * _camera.Zoom);
        Vector2 topLeft = _camera.Center - half;
        Vector2 bottomRight = _camera.Center + half;
        var rect = new Rect2(
            topLeft.X / _mapSize.X * Size.X,
            topLeft.Y / _mapSize.Y * Size.Y,
            (bottomRight.X - topLeft.X) / _mapSize.X * Size.X,
            (bottomRight.Y - topLeft.Y) / _mapSize.Y * Size.Y);

        DrawRect(rect, new Color(1, 1, 1, 0.8f), false, 1f);
    }

    private static int DecodeIndex(Color c)
    {
        int r = (int)(c.R * 255f + 0.5f);
        int g = (int)(c.G * 255f + 0.5f);
        int b = (int)(c.B * 255f + 0.5f);
        return r + g * 256 + b * 65536 - 1;
    }
}
