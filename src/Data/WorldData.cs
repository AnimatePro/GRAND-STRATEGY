using System.Collections.Generic;
using GrandStrategy.Core;

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
    public ReligionData[] Religions = System.Array.Empty<ReligionData>();
    public CultureData[] Cultures = System.Array.Empty<CultureData>();

    /// <summary>Названия провинций (индекс = id провинции), EN и RU.</summary>
    public string[] ProvinceNamesEn = System.Array.Empty<string>();
    public string[] ProvinceNamesRu = System.Array.Empty<string>();

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

    /// <summary>Название провинции на текущем языке (fallback — EN, затем id).</summary>
    public string ProvinceName(int provinceId, string language = "en")
    {
        if (provinceId >= 0 && provinceId < ProvinceNamesEn.Length)
        {
            if (language == "ru" && !string.IsNullOrEmpty(ProvinceNamesRu[provinceId]))
                return ProvinceNamesRu[provinceId];
            if (!string.IsNullOrEmpty(ProvinceNamesEn[provinceId]))
                return ProvinceNamesEn[provinceId];
        }
        return provinceId.ToString();
    }

    public int CountryByCode(string code) =>
        CountryCodeToId.TryGetValue(code, out int id) ? id : -1;

    /// <summary>Название страны на указанном языке (fallback — EN, затем код).</summary>
    public string CountryName(CountryData country, string language = "en")
    {
        if (country == null)
            return string.Empty;
        if (language == "ru" && !string.IsNullOrEmpty(country.NameRu))
            return country.NameRu;
        if (!string.IsNullOrEmpty(country.NameKey))
            return country.NameKey;
        return country.Code;
    }

    /// <summary>Название религии на текущем языке (fallback — EN, затем id).</summary>
    public string ReligionName(int id, string language = "en")
    {
        if (id >= 0 && id < Religions.Length && !string.IsNullOrEmpty(Religions[id].NameKey))
            return Core.LocalizationManager.Instance.Get(Religions[id].NameKey);
        return id.ToString();
    }

    /// <summary>Название культуры на текущем языке (fallback — EN, затем id).</summary>
    public string CultureName(int id, string language = "en")
    {
        if (id >= 0 && id < Cultures.Length && !string.IsNullOrEmpty(Cultures[id].NameKey))
            return Core.LocalizationManager.Instance.Get(Cultures[id].NameKey);
        return id.ToString();
    }

    /// <summary>Суммарный эффект принятых законов страны (множители по умолчанию = 1.0).</summary>
    public LawData AggregateLaws(CountryData country)
    {
        var agg = new LawData { TaxMult = 1.0, MilitaryCostMult = 1.0, ResearchMult = 1.0, TradeMult = 1.0 };
        foreach (int lawId in country.Laws)
        {
            if (lawId < 0 || lawId >= Laws.Length)
                continue;
            LawData l = Laws[lawId];
            agg.TaxMult *= l.TaxMult;
            agg.ConscriptionRate += l.ConscriptionRate;
            agg.StabilityBonus += l.StabilityBonus;
            agg.MilitaryCostMult *= l.MilitaryCostMult;
            agg.ResearchMult *= l.ResearchMult;
            agg.TradeMult *= l.TradeMult;
            agg.UpkeepPerTurn += l.UpkeepPerTurn;
        }
        return agg;
    }

    public int ProvinceCount => Provinces.Length;
    public int CountryCount => Countries.Length;
    public int GoodCount => Goods.Length;
}
