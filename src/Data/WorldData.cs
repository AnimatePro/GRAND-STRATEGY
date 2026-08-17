using System.Collections.Generic;

namespace GrandStrategy.Data;

/// <summary>
/// Контейнер всего мирового состояния данных (статическая часть).
/// Провинции хранятся в плотном массиве (struct), страны/товары/здания — по индексу id.
/// Строится один раз при загрузке мира, мутируется симуляцией.
/// </summary>
public sealed class WorldData
{
    public ProvinceData[] Provinces = System.Array.Empty<ProvinceData>();
    public CountryData[] Countries = System.Array.Empty<CountryData>();
    public GoodData[] Goods = System.Array.Empty<GoodData>();
    public CurrencyData[] Currencies = System.Array.Empty<CurrencyData>();
    public BuildingData[] Buildings = System.Array.Empty<BuildingData>();
    public GovernmentData[] Governments = System.Array.Empty<GovernmentData>();
    public LawData[] Laws = System.Array.Empty<LawData>();

    /// <summary>Провинция id -> индекс в Provinces (для O(1) доступа).</summary>
    public Dictionary<int, int> ProvinceIdToIndex = new();

    /// <summary>ISO-код страны -> id страны.</summary>
    public Dictionary<string, int> CountryCodeToId = new();

    // --- Мировые габариты карты (в проекционных пикселях) -------------------

    public float MapWidthPx = 1f;
    public float MapHeightPx = 1f;

    // --- Доступ -----------------------------------------------------------------

    public ProvinceData GetProvince(int id)
    {
        return ProvinceIdToIndex.TryGetValue(id, out int idx) ? Provinces[idx] : default;
    }

    public bool TryGetProvince(int id, out ProvinceData province)
    {
        if (ProvinceIdToIndex.TryGetValue(id, out int idx))
        {
            province = Provinces[idx];
            return true;
        }
        province = default;
        return false;
    }

    public void SetProvince(int id, in ProvinceData province)
    {
        if (ProvinceIdToIndex.TryGetValue(id, out int idx))
            Provinces[idx] = province;
    }

    public ref ProvinceData GetProvinceRef(int id)
    {
        if (ProvinceIdToIndex.TryGetValue(id, out int idx))
            return ref Provinces[idx];
        throw new System.Collections.Generic.KeyNotFoundException($"Province {id} not found");
    }

    public CountryData GetCountry(int id) =>
        id >= 0 && id < Countries.Length ? Countries[id] : null!;

    public bool TryGetCountry(int id, out CountryData country)
    {
        if (id >= 0 && id < Countries.Length && Countries[id] != null && Countries[id].IsAlive)
        {
            country = Countries[id];
            return true;
        }
        country = null!;
        return false;
    }

    public GoodData GetGood(int id) =>
        id >= 0 && id < Goods.Length ? Goods[id] : default;

    public int CountryByCode(string code) =>
        CountryCodeToId.TryGetValue(code, out int id) ? id : -1;

    public int ProvinceCount => Provinces.Length;
    public int CountryCount => Countries.Length;
    public int GoodCount => Goods.Length;
}
