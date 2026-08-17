using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;
using GrandStrategy.Map;
using GrandStrategy.Systems.Military;

namespace GrandStrategy;

/// <summary>
/// Корневой узел игровой сцены (scenes/Game.tscn). Программно собирает слой карты,
/// камеру, пикер, метки, миникарту и отладочный оверлей, связывает выбор провинции.
/// _Process использует только для трансформа камеры и меток; симуляция — в TickManager (M5+).
/// </summary>
public partial class GameRoot : Node
{
    private MapRenderer _mapRenderer = null!;
    private CameraRig _camera = null!;
    private ProvincePicker _picker = null!;
    private LabelPool _labels = null!;
    private Minimap _minimap = null!;
    private ArmyLayer _armyLayer = null!;
    private MapModeController _modeController = null!;
    private DebugOverlay _debug = null!;
    private Control _uiRoot = null!;

    private int _selectedProvince = -1;
    private int _hoverProvince = -1;
    private Vector2 _lastMousePos = new(float.NaN, float.NaN);

    public override void _Ready()
    {
        // 1. Данные (уже загружены на boot; перезагружаем только если их нет).
        if (!DataManager.Instance.IsLoaded)
        {
            if (!DataManager.Instance.LoadWorldData())
            {
                ShowFatal("World data not found.\nRun MapImporterTool to generate data/cache/world.json");
                return;
            }
        }
        DataManager.Instance.ValidateData();
        LogService.Instance.Info(DataManager.Instance.DumpStats());

        // 2. Камера (нужна меткам и миникарте).
        _camera = new CameraRig();
        AddChild(_camera);
        _camera.SetWorldSize(DataManager.Instance.World != null
            ? new Vector2(DataManager.Instance.World.MapWidthPx, DataManager.Instance.World.MapHeightPx)
            : new Vector2(2880f, 1440f));

        // 3. Сцена.
        BuildScene();

        // 4. Пикер (нужна ID-карта из рендера).
        _picker = new ProvincePicker();
        _picker.Initialize(_mapRenderer.IdImage);
        AddChild(_picker);

        // 5. Контроллер режимов + подписки.
        _modeController = new MapModeController();
        AddChild(_modeController);
        _modeController.Initialize(_mapRenderer, _minimap);
        _camera.MapClicked += OnMapClicked;
        _debug.ExternalCommand = HandleDebugCommand;

        // Панель режимов карты (снизу).
        var modeBar = new MapModeBar();
        modeBar.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _uiRoot.AddChild(modeBar);
        modeBar.Initialize(_modeController);

        // 6. Стартовая камера (вся карта в кадре).
        _camera.Center = _mapRenderer.WorldSize / 2f;
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        _camera.SetZoom(Mathf.Min(vp.X / _mapRenderer.WorldSize.X, vp.Y / _mapRenderer.WorldSize.Y));

        EventBus.Instance.UINotification += OnNotification;

        // 7. HUD (игра уже запущена из экрана новой игры/загрузки).
        GrandStrategy.UI.UIManager.Instance.EnsureHud();

        LogService.Instance.Info("GameRoot ready");
    }

    private void BuildScene()
    {
        _uiRoot = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _uiRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_uiRoot);

        // Карта.
        _mapRenderer = new MapRenderer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _mapRenderer.Initialize(DataManager.Instance.World);
        _uiRoot.AddChild(_mapRenderer);

        // Метки (полноэкранный слой поверх карты).
        _labels = new LabelPool();
        _labels.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _uiRoot.AddChild(_labels);
        _labels.Initialize(DataManager.Instance.World, _camera);

        // Маркеры армий (поверх меток).
        _armyLayer = new ArmyLayer();
        _armyLayer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _uiRoot.AddChild(_armyLayer);
        _armyLayer.Initialize(DataManager.Instance.World, _camera);

        // Миникарта (правый нижний угол).
        _minimap = new Minimap();
        _minimap.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _minimap.OffsetLeft = -260;
        _minimap.OffsetTop = -130;
        _minimap.OffsetRight = -8;
        _minimap.OffsetBottom = -8;
        _uiRoot.AddChild(_minimap);
        _minimap.Initialize(_mapRenderer, _camera);

        // Отладка.
        _debug = new DebugOverlay();
        _debug.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _debug.OffsetLeft = 8;
        _debug.OffsetTop = 8;
        _uiRoot.AddChild(_debug);
    }

    public override void _ExitTree()
    {
        // Возврат в меню — прячем HUD (он живёт в автозагрузке и переживает смены сцен).
        GrandStrategy.UI.UIManager.Instance.HideHud();
        EventBus.Instance.UINotification -= OnNotification;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is not InputEventKey key || !key.Pressed || key.Echo)
            return;

        switch (key.Keycode)
        {
            case Key.Escape:
                GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
                GetViewport().SetInputAsHandled();
                break;

            case Key.Space:
                // Пробел — пауза/снятие с паузы.
                if (GameManager.Instance.State == GameState.Playing)
                    GameManager.Instance.Pause();
                else if (GameManager.Instance.State == GameState.Paused)
                    GameManager.Instance.Resume();
                GetViewport().SetInputAsHandled();
                break;

            case Key.Key1:
            case Key.Key2:
            case Key.Key3:
            case Key.Key4:
            case Key.Key5:
                // 1..5 — скорость времени.
                TimeManager.Instance.SetSpeed((int)(key.Keycode - Key.Key1) + 1);
                TimeManager.Instance.Resume();
                GameManager.Instance.Resume();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (_mapRenderer == null || _camera == null)
            return;

        // Трансформ камеры -> позиция/размер TextureRect.
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        _mapRenderer.Position = vp / 2f - _camera.Center * _camera.Zoom;
        _mapRenderer.Size = _mapRenderer.WorldSize * _camera.Zoom;

        // Ховер.
        Vector2 mouse = GetViewport().GetMousePosition();
        if (mouse != _lastMousePos)
        {
            _lastMousePos = mouse;
            int hover = _picker.WorldToProvinceIndex(_camera.ScreenToWorld(mouse));
            if (hover != _hoverProvince)
            {
                _hoverProvince = hover;
                _mapRenderer.SetHover(hover);
            }
        }

        // Метки, миникарта и маркеры армий.
        _labels.Update(delta);
        _minimap.QueueRedraw();
        _armyLayer.QueueRedraw();
    }

    private void OnMapClicked(Vector2 world)
    {
        int index = _picker.WorldToProvinceIndex(world);
        if (index < 0)
        {
            ClearSelection();
            return;
        }

        _selectedProvince = index;
        _mapRenderer.SetSelected(index);
        _debug.SetSelectedProvince(index);

        ProvinceData p = DataManager.Instance.World.GetProvince(index);
        EventBus.Instance.EmitProvinceSelected(index);

        if (p.OwnerId >= 0)
        {
            _mapRenderer.SetHighlightCountry(p.OwnerId);
            EventBus.Instance.EmitCountrySelected(p.OwnerId);
            CountryData c = DataManager.Instance.World.GetCountry(p.OwnerId);
            EventBus.Instance.EmitUINotification($"{c.NameKey} ({c.Code}) — pop {c.Population:N0}");
        }
        else
        {
            _mapRenderer.SetHighlightCountry(-1);
        }
    }

    private void ClearSelection()
    {
        _selectedProvince = -1;
        _mapRenderer.SetSelected(-1);
        _mapRenderer.SetHighlightCountry(-1);
        _debug.SetSelectedProvince(-1);
    }

    private void OnNotification(string text) =>
        LogService.Instance.Info($"[notification] {text}");

    private int PlayerCountryId() => GameManager.Instance.ActiveOptions?.PlayerCountryId ?? 0;

    private void HandleDebugCommand(string text)
    {
        string[] parts = text.Trim().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        switch (parts[0].ToLowerInvariant())
        {
            case "set_map_mode":
                if (parts.Length > 1 && int.TryParse(parts[1], out int mode))
                    _modeController.SetMode((MapMode)Mathf.Clamp(mode, 0, 11));
                break;
            case "add_money":
                if (parts.Length > 1 && double.TryParse(parts[1],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double amt))
                    DataManager.Instance.World.GetCountry(PlayerCountryId()).Treasury += amt;
                break;
            case "spawn_army":
                {
                    var world = DataManager.Instance.World;
                    var country = world.GetCountry(PlayerCountryId());
                    if (country != null && country.CapitalProvinceId >= 0)
                        MilitaryManager.Instance.RecruitArmy(PlayerCountryId(), country.CapitalProvinceId,
                            new System.Collections.Generic.Dictionary<int, int> { { 0, 5 }, { 1, 2 }, { 2, 2 } });
                    break;
                }
            case "test_save":
                GameManager.Instance.SaveGame("debug_roundtrip");
                bool ok = GameManager.Instance.LoadGame("debug_roundtrip");
                GD.Print($"round-trip save test: {(ok ? "OK" : "FAILED")}");
                break;
            default:
                GD.Print($"Unknown command: {parts[0]}");
                break;
        }
    }

    private void ShowFatal(string message)
    {
        LogService.Instance.Error(message);
        var label = new Label
        {
            Text = message,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(label);
    }
}
