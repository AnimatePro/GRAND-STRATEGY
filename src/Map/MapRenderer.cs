using System;
using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;

namespace GrandStrategy.Map;

/// <summary>
/// Рендер карты: один TextureRect с ShaderMaterial (assets/map/map.gdshader).
/// Загружает ID-карту и маску границ из data/cache/*.png как сырые Image
/// (в обход импорта текстур — гарантирует точные значения байт), строит
/// color_lut/country_lut (1 пиксель на провинцию) и обновляет их по режиму карты.
/// Мутации вида (цвет страны, режим) — только через SetMapMode/RefreshColors.
/// </summary>
public partial class MapRenderer : TextureRect
{
    public const string IdMapPath = "res://data/cache/id_map.png";
    public const string BorderMaskPath = "res://data/cache/border_mask.png";

    private ShaderMaterial _material = null!;
    private ImageTexture _idTexture = null!;
    private ImageTexture _borderTexture = null!;
    private ImageTexture _colorLut = null!;
    private ImageTexture _countryLut = null!;
    private Image _colorLutImage = null!;
    private Image _countryLutImage = null!;
    private Image _idImage = null!;

    /// <summary>Сырая ID-карта (для пикера и миникарты).</summary>
    public Image IdImage => _idImage;

    public Vector2 WorldSize => _world != null ? new Vector2(_world.MapWidthPx, _world.MapHeightPx) : Vector2.One;

    public Color ProvinceColor(int index) => _colorLutImage.GetPixel(index, 0);

    private WorldData? _world;
    private MapMode _mode = MapMode.Political;
    private int _hoverId = -1;
    private int _selectedId = -1;
    private int _highlightCountry = -1;

    public MapMode Mode => _mode;

    public void Initialize(WorldData world)
    {
        _world = world;

        // ID-карта (сырая загрузка).
        _idImage = Image.LoadFromFile(ProjectSettings.GlobalizePath(IdMapPath));
        _idTexture = ImageTexture.CreateFromImage(_idImage);
        Texture = _idTexture;
        TextureFilter = TextureFilterEnum.Nearest;
        StretchMode = TextureRect.StretchModeEnum.Scale;
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;

        Image borderImg = Image.LoadFromFile(ProjectSettings.GlobalizePath(BorderMaskPath));
        _borderTexture = ImageTexture.CreateFromImage(borderImg);

        // LUT: 1 пиксель на провинцию.
        int n = world.ProvinceCount;
        _colorLutImage = Image.CreateEmpty(Math.Max(n, 1), 1, false, Image.Format.Rgba8);
        _countryLutImage = Image.CreateEmpty(Math.Max(n, 1), 1, false, Image.Format.Rgba8);

        BuildCountryLut();
        BuildColorLut();

        _colorLut = ImageTexture.CreateFromImage(_colorLutImage);
        _countryLut = ImageTexture.CreateFromImage(_countryLutImage);

        _material = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://assets/map/map.gdshader"),
        };
        _material.SetShaderParameter("color_lut", _colorLut);
        _material.SetShaderParameter("country_lut", _countryLut);
        _material.SetShaderParameter("border_mask", _borderTexture);
        _material.SetShaderParameter("province_count", (float)n);
        _material.SetShaderParameter("texel_size", new Vector2(1f / idImg.GetWidth(), 1f / idImg.GetHeight()));
        Material = _material;

        SetMapMode(MapMode.Political);

        // Живое обновление динамических режимов карты.
        EventBus.Instance.EconomyUpdated += OnDataChanged;
        EventBus.Instance.DiplomacyUpdated += OnDataChanged;
        EventBus.Instance.WarDeclared += OnWarDeclared;
        EventBus.Instance.PeaceSigned += OnPeaceSigned;
    }

    public override void _ExitTree()
    {
        EventBus.Instance.EconomyUpdated -= OnDataChanged;
        EventBus.Instance.DiplomacyUpdated -= OnDataChanged;
        EventBus.Instance.WarDeclared -= OnWarDeclared;
        EventBus.Instance.PeaceSigned -= OnPeaceSigned;
    }

    private void OnWarDeclared(int a, int b) => OnDataChanged();
    private void OnPeaceSigned(int a, int b) => OnDataChanged();

    private void OnDataChanged()
    {
        if (_mode is MapMode.Trade or MapMode.Diplomacy or MapMode.War or MapMode.Economy or MapMode.Population)
            BuildColorLut();
    }

    /// <summary>Заполняет country_lut: провинция -> (countryId+1).</summary>
    private void BuildCountryLut()
    {
        if (_world == null)
            return;
        for (int i = 0; i < _world.ProvinceCount; i++)
        {
            ProvinceData p = _world.Provinces[i];
            int cid = p.OwnerId >= 0 ? p.OwnerId + 1 : 0;
            _countryLutImage.SetPixel(i, 0, Encode(cid));
        }
        _countryLut?.Update(_countryLutImage);
    }

    /// <summary>Заполняет color_lut согласно текущему режиму карты.</summary>
    private void BuildColorLut()
    {
        if (_world == null)
            return;
        for (int i = 0; i < _world.ProvinceCount; i++)
        {
            _colorLutImage.SetPixel(i, 0, ColorForMode(_world.Provinces[i]));
        }
        _colorLut?.Update(_colorLutImage);
    }

    private Color ColorForMode(ProvinceData p)
    {
        switch (_mode)
        {
            case MapMode.Political:
                if (p.OwnerId >= 0)
                    return _world!.Countries[p.OwnerId].Color;
                return Colors.DimGray;

            case MapMode.Terrain:
                return TerrainColor(p.Terrain);

            case MapMode.Population:
                return GradientByValue(p.TotalPopulation, 0, 10_000_000);

            case MapMode.Economy:
                return _world!.Countries.Length > 0 && p.OwnerId >= 0
                    ? GradientByValue((float)_world.Countries[p.OwnerId].Gdp, 0, 20_000_000_000)
                    : Colors.DimGray;

            case MapMode.Development:
                return GradientByValue(p.Development, 0, 1);

            case MapMode.Infrastructure:
                return GradientByValue(p.Infrastructure, 0, 1);

            case MapMode.Unrest:
                return GradientByValue(p.Unrest, 0, 1);

            case MapMode.Resources:
                return p.ResourceIds.Length > 0
                    ? new Color(0.5f, 0.4f, 0.15f)
                    : new Color(0.25f, 0.3f, 0.25f);

            case MapMode.Trade:
                {
                    var eco = GrandStrategy.Systems.Economy.EconomyManager.Instance.Economy;
                    if (p.OwnerId >= 0 && p.OwnerId < eco.Countries.Length)
                    {
                        double tb = eco.Countries[p.OwnerId].TradeBalance;
                        if (tb > 0) return new Color(0.2f, 0.6f, 0.3f);
                        if (tb < 0) return new Color(0.75f, 0.15f, 0.1f);
                        return new Color(0.5f, 0.5f, 0.5f);
                    }
                    return Colors.DimGray;
                }

            case MapMode.Diplomacy:
                if (p.OwnerId >= 0)
                {
                    int playerId = GrandStrategy.Core.GameManager.Instance.ActiveOptions?.PlayerCountryId ?? -1;
                    if (playerId >= 0 && p.OwnerId == playerId)
                        return new Color(0.2f, 0.5f, 0.9f);
                    if (playerId >= 0)
                    {
                        float rel = _world!.Countries[p.OwnerId].RelationWith(playerId);
                        if (rel > 30) return new Color(0.2f, 0.7f, 0.3f);
                        if (rel < -30) return new Color(0.8f, 0.2f, 0.2f);
                        return new Color(0.6f, 0.6f, 0.5f);
                    }
                }
                return Colors.DimGray;

            case MapMode.War:
                if (p.OwnerId >= 0 && GrandStrategy.Systems.Diplomacy.DiplomacyManager.Instance != null)
                {
                    foreach (var w in GrandStrategy.Systems.Diplomacy.DiplomacyManager.Instance.Wars)
                        if (w.AttackerId == p.OwnerId || w.DefenderId == p.OwnerId)
                            return new Color(0.75f, 0.1f, 0.1f);
                    return new Color(0.4f, 0.45f, 0.4f);
                }
                return Colors.DimGray;

            default:
                return Colors.SlateGray;
        }
    }

    public void SetMapMode(MapMode mode)
    {
        _mode = mode;
        BuildColorLut();
    }

    /// <summary>Обновить цвета стран (после смены цвета/оккупации/присоединения).</summary>
    public void RefreshColors()
    {
        BuildCountryLut();
        BuildColorLut();
    }

    public void SetHover(int provinceIndex)
    {
        if (_hoverId == provinceIndex)
            return;
        _hoverId = provinceIndex;
        _material.SetShaderParameter("hover_id", provinceIndex);
    }

    public void SetSelected(int provinceIndex)
    {
        if (_selectedId == provinceIndex)
            return;
        _selectedId = provinceIndex;
        _material.SetShaderParameter("selected_id", provinceIndex);
    }

    public void SetHighlightCountry(int countryId)
    {
        if (_highlightCountry == countryId)
            return;
        _highlightCountry = countryId;
        _material.SetShaderParameter("highlight_country", countryId);
    }

    private static Color Encode(int id)
    {
        int r = id & 0xFF, g = (id >> 8) & 0xFF, b = (id >> 16) & 0xFF;
        return new Color(r / 255f, g / 255f, b / 255f);
    }

    private static Color TerrainColor(Terrain t)
    {
        return t switch
        {
            Terrain.Plains => new Color(0.55f, 0.72f, 0.42f),
            Terrain.Hills => new Color(0.62f, 0.62f, 0.42f),
            Terrain.Mountains => new Color(0.55f, 0.52f, 0.48f),
            Terrain.Forest => new Color(0.28f, 0.48f, 0.28f),
            Terrain.Jungle => new Color(0.16f, 0.42f, 0.24f),
            Terrain.Desert => new Color(0.82f, 0.76f, 0.5f),
            Terrain.Tundra => new Color(0.72f, 0.74f, 0.7f),
            Terrain.Marsh => new Color(0.45f, 0.55f, 0.45f),
            Terrain.Coast => new Color(0.55f, 0.68f, 0.62f),
            _ => new Color(0.2f, 0.3f, 0.5f),
        };
    }

    /// <summary>Линейный градиент белый->зелёный->красный для тепловых режимов.</summary>
    private static Color GradientByValue(float value, float min, float max)
    {
        float t = Mathf.Clamp((value - min) / Mathf.Max(max - min, 1e-6f), 0f, 1f);
        if (t < 0.5f)
            return Colors.White.Lerp(new Color(0.2f, 0.6f, 0.3f), t * 2f);
        return new Color(0.2f, 0.6f, 0.3f).Lerp(new Color(0.75f, 0.15f, 0.1f), (t - 0.5f) * 2f);
    }
}
