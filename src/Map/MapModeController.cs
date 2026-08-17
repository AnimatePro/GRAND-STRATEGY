using System.Collections.Generic;
using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;

namespace GrandStrategy.Map;

/// <summary>
/// Управление режимами карты. Переключение: клавиши F1..F8 (8 основных режимов),
/// клавиша M — циклический перебор. Кнопки панели (MapModeBar) вызывают SetMode.
/// </summary>
public partial class MapModeController : Node
{
    private MapRenderer? _renderer;
    private Minimap? _minimap;
    private int _modeIndex;

    /// <summary>Основные режимы (привязаны к F1..F8).</summary>
    public static readonly MapMode[] Order =
    {
        MapMode.Political, MapMode.Terrain, MapMode.Population, MapMode.Economy,
        MapMode.Trade, MapMode.Resources, MapMode.Development, MapMode.Infrastructure,
        MapMode.Diplomacy, MapMode.War, MapMode.Unrest, MapMode.Religion, MapMode.Culture,
    };

    public MapMode CurrentMode => Order[Mathf.Clamp(_modeIndex, 0, Order.Length - 1)];

    public void Initialize(MapRenderer renderer, Minimap minimap)
    {
        _renderer = renderer;
        _minimap = minimap;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey key && key.Pressed && !key.Echo)
        {
            // F1..F8 — 8 основных режимов.
            if (key.Keycode >= Key.F1 && key.Keycode <= Key.F8)
            {
                int idx = (int)(key.Keycode - Key.F1);
                if (idx < Order.Length)
                    SetMode(Order[idx]);
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.M)
            {
                Cycle();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public void SetMode(MapMode mode)
    {
        _renderer?.SetMapMode(mode);
        _modeIndex = System.Array.IndexOf(Order, mode);
        if (_modeIndex < 0)
            _modeIndex = 0;
        _minimap?.Rebuild();
        EventBus.Instance.EmitUINotification($"Map mode: {mode}");
    }

    public void Cycle()
    {
        _modeIndex = (_modeIndex + 1) % Order.Length;
        SetMode(Order[_modeIndex]);
    }
}
