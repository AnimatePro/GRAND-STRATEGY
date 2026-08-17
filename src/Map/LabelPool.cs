using System.Collections.Generic;
using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;

namespace GrandStrategy.Map;

/// <summary>
/// Пул меток названий провинций/столиц. Показ по порогу зума, приоритет столицам,
/// скрытие при дальнем зуме, переиспользование Label-узлов (без аллокаций на кадр).
/// </summary>
public partial class LabelPool : Control
{
    public const float LabelMinZoom = 1.6f;   // зум, при котором видны обычные метки
    public const float CapitalMinZoom = 0.5f; // зум, при котором видны столицы
    public const int MaxLabels = 300;

    private WorldData? _world;
    private CameraRig? _camera;
    private readonly List<Label> _pool = new();
    private readonly List<int> _visible = new();

    public void Initialize(WorldData world, CameraRig camera)
    {
        _world = world;
        _camera = camera;
        MouseFilter = Control.MouseFilterEnum.Ignore;
        for (int i = 0; i < MaxLabels; i++)
        {
            var label = new Label
            {
                Visible = false,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            label.AddThemeFontSizeOverride("font_size", 12);
            AddChild(label);
            _pool.Add(label);
        }
    }

    public void Update(double delta)
    {
        if (_world == null || _camera == null)
            return;

        _visible.Clear();
        float zoom = _camera.Zoom;

        // Пороговый зум: ниже CapitalMinZoom ничего не показываем.
        if (zoom < CapitalMinZoom)
        {
            foreach (Label l in _pool)
                l.Visible = false;
            return;
        }

        bool showAll = zoom >= LabelMinZoom;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        Vector2 half = size / (2f * zoom);
        Vector2 center = _camera.Center;
        float minX = center.X - half.X - 40, maxX = center.X + half.X + 40;
        float minY = center.Y - half.Y - 20, maxY = center.Y + half.Y + 20;

        // Капиталы всегда (при достаточном зуме), прочие — по порогу.
        for (int i = 0; i < _world.ProvinceCount && _visible.Count < MaxLabels; i++)
        {
            ProvinceData p = _world.Provinces[i];
            Vector2 c = p.Centroid;
            if (c.X < minX || c.X > maxX || c.Y < minY || c.Y > maxY)
                continue;

            bool isCapital = p.OwnerId >= 0 && _world.Countries[p.OwnerId].CapitalProvinceId == p.Id;
            if (!showAll && !isCapital)
                continue;

            _visible.Add(p.Id);
        }

        // Сопоставление пула.
        for (int i = 0; i < _pool.Count; i++)
        {
            Label label = _pool[i];
            if (i < _visible.Count)
            {
                int pid = _visible[i];
                ProvinceData p = _world.Provinces[pid];
                label.Text = ProvinceLabel(p);
                label.Position = _camera.WorldToScreen(p.Centroid) - label.Size / 2f;
                label.Visible = true;
            }
            else
            {
                label.Visible = false;
            }
        }
    }

    private string ProvinceLabel(ProvinceData p)
    {
        string name = _world!.ProvinceName(p.Id, LocalizationManager.Instance.Language);
        if (name == p.Id.ToString())
            name = string.Empty; // нет названия — показываем только население
        if (p.TotalPopulation > 1_000_000)
            return name.Length > 0 ? $"{name}" : $"{p.TotalPopulation / 1_000_000f:0.0}M";
        return name;
    }
}
