using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;
using GrandStrategy.Systems.Diplomacy;
using GrandStrategy.Systems.Military;

namespace GrandStrategy.Map;

/// <summary>
/// Слой отображения армий: маркер (круг цвета страны + число юнитов) со сглаженным
/// движением между центроидами провинций, стрелка направления и штриховка оккупации.
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
        int playerId = GameManager.Instance.ActiveOptions?.PlayerCountryId ?? -1;

        // Штриховка оккупированных провинций (видимая игроку).
        for (int i = 0; i < _world.ProvinceCount; i++)
        {
            ProvinceData p = _world.Provinces[i];
            if (p.ControllerId >= 0 && p.ControllerId != p.OwnerId &&
                (p.ControllerId == playerId || p.OwnerId == playerId))
            {
                Vector2 c = _camera.WorldToScreen(p.Centroid);
                DrawCircle(c, 6f, new Color(0, 0, 0, 0.25f));
                DrawLine(c - new Vector2(5, 5), c + new Vector2(5, 5), new Color(1, 1, 1, 0.4f));
                DrawLine(c - new Vector2(-5, 5), c + new Vector2(-5, -5), new Color(1, 1, 1, 0.4f));
            }
        }

        foreach (ArmyData army in MilitaryManager.Instance.Armies)
        {
            ProvinceData cur = _world.GetProvince(army.ProvinceId);
            if (cur.Id < 0)
                continue;

            // Позиция со сглаживанием: интерполяция между текущей и следующей провинцией.
            Vector2 pos = cur.Centroid;
            if (army.MoveOrder.Count > 0)
            {
                ProvinceData next = _world.GetProvince(army.MoveOrder[0]);
                if (next.Id >= 0)
                    pos = cur.Centroid.Lerp(next.Centroid, Mathf.Clamp(army.MoveProgress, 0f, 1f));

                // Стрелка направления движения.
                Vector2 dir = (next.Centroid - cur.Centroid).Normalized();
                Vector2 s = _camera.WorldToScreen(pos);
                DrawLine(s, s + dir * 14f, new Color(1, 1, 1, 0.8f), 2f);
            }

            Vector2 screen = _camera.WorldToScreen(pos);
            Color color = army.OwnerId >= 0 && army.OwnerId < _world.Countries.Length
                ? _world.Countries[army.OwnerId].Color
                : Colors.White;

            DrawCircle(screen, 7f, color);
            DrawCircle(screen, 7f, new Color(0, 0, 0, 0.6f), false, 1.5f);
            string text = army.TotalUnits.ToString();
            Vector2 ts = font.GetStringSize(text, HorizontalAlignment.Left, -1, 12);
            DrawString(font, screen - new Vector2(ts.X / 2f, -14f), text,
                HorizontalAlignment.Left, -1, 12, Colors.White);

            // Полоса здоровья (сила).
            if (army.Strength < 1f)
            {
                DrawRect(new Rect2(screen.X - 10, screen.Y + 10, 20, 3), new Color(0, 0, 0, 0.6f));
                DrawRect(new Rect2(screen.X - 10, screen.Y + 10, 20 * army.Strength, 3), Colors.Green);
            }
        }
    }
}
