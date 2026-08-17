using System;
using System.Text;
using Godot;
using GrandStrategy.Data;

namespace GrandStrategy.Core;

/// <summary>
/// Отладочный оверлей: FPS, время кадра, аллокации GC, информация о выбранной провинции
/// и консоль команд. Активируется F3, консоль — клавишей `/~. Команды, не разобранные
/// локально, делегируются в ExternalCommand (регистрируется GameRoot).
/// </summary>
public partial class DebugOverlay : Control
{
    private Label _text = null!;
    private LineEdit _console = null!;
    private VBoxContainer _box = null!;
    private readonly StringBuilder _sb = new();

    private float _fps;
    private float _frameMs;
    private int _selectedProvince = -1;

    /// <summary>Внешний обработчик команд (set_map_mode, add_money, spawn_army...).</summary>
    public Action<string>? ExternalCommand;

    public override void _Ready()
    {
        _box = new VBoxContainer();
        AddChild(_box);

        _text = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _text.AddThemeFontSizeOverride("font_size", 13);
        _box.AddChild(_text);

        _console = new LineEdit { PlaceholderText = "console: type 'help'", Visible = false };
        _console.TextSubmitted += OnCommand;
        _box.AddChild(_console);

        Visible = false;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        _fps = Engine.GetFramesPerSecond();
        _frameMs = (float)(delta * 1000.0);
        _sb.Clear();
        _sb.AppendLine($"FPS: {_fps:0}  frame: {_frameMs:0.00} ms");
        _sb.AppendLine($"objects: {Performance.GetMonitor(Performance.Monitor.ObjectCount)}");

        if (_selectedProvince >= 0 && DataManager.Instance.IsLoaded)
        {
            ProvinceData p = DataManager.Instance.World.GetProvince(_selectedProvince);
            _sb.AppendLine($"prov {p.Id}: pop={p.TotalPopulation:N0} owner={p.OwnerId}");
            _sb.AppendLine($"  adults={p.WorkingAgePopulation:N0} neighbors={p.NeighborIds.Length}");
        }
        _text.Text = _sb.ToString();
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.F3)
        {
            Visible = !Visible;
            GetViewport().SetInputAsHandled();
        }
        else if (Visible && ev is InputEventKey k2 && k2.Pressed && !k2.Echo &&
                 (k2.Keycode == Key.Quoteleft || k2.Keycode == Key.AsciiTilde))
        {
            _console.Visible = !_console.Visible;
            if (_console.Visible)
                _console.GrabFocus();
            GetViewport().SetInputAsHandled();
        }
    }

    public void SetSelectedProvince(int id) => _selectedProvince = id;

    private void OnCommand(string text)
    {
        _console.Clear();
        _console.Visible = false;
        if (string.IsNullOrWhiteSpace(text))
            return;

        string[] parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (parts[0].ToLowerInvariant())
        {
            case "help":
                GD.Print("dump_stats | set_map_mode N | add_money X | spawn_army");
                return;
            case "dump_stats":
                GD.Print(DataManager.Instance.DumpStats());
                return;
        }

        if (ExternalCommand != null)
            ExternalCommand(text);
        else
            GD.Print($"Unknown command: {parts[0]}");
    }
}
