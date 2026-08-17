using System;
using Godot;
using GrandStrategy.Core;

namespace GrandStrategy.Map;

/// <summary>
/// Камера карты (без Camera2D): центр в мировых пикселях + зум (экранный пиксель на мировой).
/// Ввод: перетаскивание ЛКМ/СКМ/ПКМ, WASD/стрелки, колесо к центру курсора, edge-scroll.
/// Интеграция в _Process/_UnhandledInput (не в симуляции). Событие MapClicked — чистый клик без драга.
/// </summary>
public partial class CameraRig : Node
{
    public const float MinZoom = 0.15f;
    public const float MaxZoom = 12f;

    public Vector2 Center = new(1440f, 720f);
    public float Zoom { get; private set; } = 1f;

    /// <summary>Чистый клик по карте (мировые координаты), если не было перетаскивания.</summary>
    public event Action<Vector2>? MapClicked;

    private bool _dragging;
    private bool _moved;
    private Vector2 _dragStartScreen;
    private Vector2 _dragStartCenter;
    private Vector2 _worldSize = new(2880f, 1440f);

    private const float PanSpeed = 1400f;      // px/с при клавишах
    private const float EdgeMargin = 16f;      // px от края для edge-scroll
    private const float DragThreshold = 6f;    // px до срабатывания драга

    public void SetWorldSize(Vector2 size) => _worldSize = size;

    /// <summary>Установка зума с клампингом (стартовое позиционирование).</summary>
    public void SetZoom(float zoom)
    {
        Zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
        ClampCenter();
    }

    public Vector2 ScreenToWorld(Vector2 screen)
    {
        Vector2 viewportCenter = GetViewport().GetVisibleRect().Size / 2f;
        return (screen - viewportCenter) / Zoom + Center;
    }

    public Vector2 WorldToScreen(Vector2 world)
    {
        Vector2 viewportCenter = GetViewport().GetVisibleRect().Size / 2f;
        return (world - Center) * Zoom + viewportCenter;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
        {
            ZoomAt(mb.Position, Zoom * 1.15f);
            GetViewport().SetInputAsHandled();
        }
        else if (ev is InputEventMouseButton mb2 && mb2.ButtonIndex == MouseButton.WheelDown && mb2.Pressed)
        {
            ZoomAt(mb2.Position, Zoom / 1.15f);
            GetViewport().SetInputAsHandled();
        }
        else if (ev is InputEventMouseButton mb3 && mb3.Pressed && IsPanButton(mb3.ButtonIndex))
        {
            _dragging = true;
            _moved = false;
            _dragStartScreen = mb3.Position;
            _dragStartCenter = Center;
        }
        else if (ev is InputEventMouseButton mb4 && !mb4.Pressed && IsPanButton(mb4.ButtonIndex))
        {
            if (!_moved)
                MapClicked?.Invoke(ScreenToWorld(mb4.Position));
            _dragging = false;
        }
        else if (ev is InputEventMouseMotion mm && _dragging)
        {
            Vector2 delta = mm.Position - _dragStartScreen;
            if (delta.Length() > DragThreshold)
                _moved = true;
            if (_moved)
                Center = _dragStartCenter - delta / Zoom;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Vector2 dir = Vector2.Zero;

        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;

        if (dir != Vector2.Zero)
            Center += dir.Normalized() * PanSpeed * dt / Zoom;

        // Edge scroll (опционально, из настроек).
        if (SettingsManager.Instance.Current.Game.EdgePan)
        {
            Vector2 mouse = GetViewport().GetMousePosition();
            Vector2 size = GetViewport().GetVisibleRect().Size;
            if (mouse.X <= EdgeMargin) dir.X -= 1;
            if (mouse.X >= size.X - EdgeMargin) dir.X += 1;
            if (mouse.Y <= EdgeMargin) dir.Y -= 1;
            if (mouse.Y >= size.Y - EdgeMargin) dir.Y += 1;
            if (dir != Vector2.Zero)
                Center += dir.Normalized() * PanSpeed * dt / Zoom;
        }

        ClampCenter();
    }

    private void ZoomAt(Vector2 screenPos, float newZoom)
    {
        Vector2 worldUnderCursor = ScreenToWorld(screenPos);
        Zoom = Mathf.Clamp(newZoom, MinZoom, MaxZoom);
        Vector2 viewportCenter = GetViewport().GetVisibleRect().Size / 2f;
        Center = worldUnderCursor - (screenPos - viewportCenter) / Zoom;
        ClampCenter();
    }

    private void ClampCenter()
    {
        Vector2 half = _worldSize / 2f;
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        float halfView = (viewportSize / Zoom) / 2f;
        // Разрешаем небольшой выход за края карты (океан), но ограничиваем.
        float margin = 200f;
        Center.X = Mathf.Clamp(Center.X, -margin, _worldSize.X + margin);
        Center.Y = Mathf.Clamp(Center.Y, -margin, _worldSize.Y + margin);
    }

    private static bool IsPanButton(MouseButton b) =>
        b == MouseButton.Left || b == MouseButton.Middle || b == MouseButton.Right;
}
