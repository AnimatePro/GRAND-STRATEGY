using System.Collections.Generic;
using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;

namespace GrandStrategy.Map;

/// <summary>
/// Управление режимами карты (политический, ландшафт, население и т.д.).
/// Переключает MapRenderer.SetMapMode и рассылает событие. Циклический перебор по клавише.
/// </summary>
public partial class MapModeController : Node
{
    private MapRenderer? _renderer;
    private Minimap? _minimap;
    private int _modeIndex;

    private static readonly MapMode[] Order =
    {
        MapMode.Political, MapMode.Terrain, MapMode.Population, MapMode.Economy,
        MapMode.Trade, MapMode.Resources, MapMode.Development, MapMode.Infrastructure,
        MapMode.Diplomacy, MapMode.War, MapMode.Unrest, MapMode.Debug,
    };

    public void Initialize(MapRenderer renderer, Minimap minimap)
    {
        _renderer = renderer;
        _minimap = minimap;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.M)
        {
            Cycle();
            GetViewport().SetInputAsHandled();
        }
    }

    public void SetMode(MapMode mode)
    {
        _renderer?.SetMapMode(mode);
        _modeIndex = System.Array.IndexOf(Order, mode);
        _minimap?.Rebuild();
        EventBus.Instance.EmitUINotification($"Map mode: {mode}");
    }

    public void Cycle()
    {
        _modeIndex = (_modeIndex + 1) % Order.Length;
        SetMode(Order[_modeIndex]);
    }
}
