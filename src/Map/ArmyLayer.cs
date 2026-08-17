using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;
using GrandStrategy.Systems.Military;

namespace GrandStrategy.Map;

/// <summary>
/// Слой отображения армий: рисует маркер (круг цвета страны + число юнитов) в центроиде
/// провинции каждой армии. Обновляется каждый кадр через QueueRedraw (дёшево: сотни армий).
/// </summary>
public partial class ArmyLayer : Control
{
    private WorldData? _world;
    private CameraRig? _camera;

    public override void _Ready()
    {
        MouseFilter = Control.MouseFilterEnum.Ignore;
    }

    public void Initialize(WorldData world, CameraRig camera)
    {
        _world = world;
        _camera = camera;
    }

    public override void _Draw()
    {
        if (_world == null || _camera == null)
            return;

        var font = ThemeDB.FallbackFont;
        foreach (ArmyData army in MilitaryManager.Instance.Armies)
        {
            ProvinceData p = _world.GetProvince(army.ProvinceId);
            if (p.Id < 0)
                continue;

            Vector2 screen = _camera.WorldToScreen(p.Centroid);
            Color color = army.OwnerId >= 0 && army.OwnerId < _world.Countries.Length
                ? _world.Countries[army.OwnerId].Color
                : Colors.White;

            DrawCircle(screen, 7f, color);
            DrawCircle(screen, 7f, new Color(0, 0, 0, 0.6f), false, 1.5f);
            string text = army.TotalUnits.ToString();
            Vector2 ts = font.GetStringSize(text, HorizontalAlignment.Left, -1, 12);
            DrawString(font, screen - new Vector2(ts.X / 2f, -14f), text,
                HorizontalAlignment.Left, -1, 12, Colors.White);
        }
    }
}
