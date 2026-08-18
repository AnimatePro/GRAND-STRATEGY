using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;

namespace GrandStrategy.Map;

/// <summary>
/// Всплывающая подсказка о провинции под курсором (базовый тултип, как в AoH3).
/// Показывает название, владельца, население, развитие, инфраструктуру, террейн.
/// </summary>
public partial class TooltipLayer : Control
{
    private PanelContainer _panel = null!;
    private Label _label = null!;

    public override void _Ready()
    {
        MouseFilter = Control.MouseFilterEnum.Ignore;

        _panel = new PanelContainer
        {
            Visible = false,
        };
        AddChild(_panel);

        _label = new Label();
        _label.AddThemeFontSizeOverride("font_size", 13);
        _panel.AddChild(_label);
    }

    public void ShowProvince(WorldData world, int provinceId, Vector2 screenPos)
    {
        if (_panel == null || provinceId < 0 || provinceId >= world.ProvinceCount)
        {
            HideTooltip();
            return;
        }

        ProvinceData p = world.Provinces[provinceId];
        string lang = LocalizationManager.Instance.Language;
        string owner = world.TryGetCountry(p.OwnerId, out CountryData c)
            ? world.CountryName(c, lang)
            : LocalizationManager.Instance.Get("unclaimed");

        _label.Text =
            $"{world.ProvinceName(p.Id, lang)}\n" +
            $"{owner}\n" +
            $"Pop: {p.TotalPopulation:N0}\n" +
            $"Dev: {p.Development:P0}  Infra: {p.Infrastructure:P0}\n" +
            $"Terrain: {p.Terrain}  Climate: {p.Climate}\n" +
            $"{LocalizationManager.Instance.Get("PANEL_RELIGION")}: {world.ReligionName(p.ReligionId, lang)}\n" +
            $"{LocalizationManager.Instance.Get("PANEL_CULTURE")}: {world.CultureName(p.CultureId, lang)}";

        _panel.Position = ClampToViewport(screenPos + new Vector2(16, 16), _panel.Size);
        _panel.Visible = true;
    }

    public void HideTooltip()
    {
        if (_panel != null)
            _panel.Visible = false;
    }

    private Vector2 ClampToViewport(Vector2 pos, Vector2 size)
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        float x = Mathf.Clamp(pos.X, 0f, Mathf.Max(vp.X - size.X, 0f));
        float y = Mathf.Clamp(pos.Y, 0f, Mathf.Max(vp.Y - size.Y, 0f));
        return new Vector2(x, y);
    }
}
