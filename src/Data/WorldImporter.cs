using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace GrandStrategy.Data;

/// <summary>
/// Пайплайн импорта реального мира (редакторное время, запускается MapImporterTool).
/// 1) Парсит admin-0 (страны) и admin-1 (провинции/регионы) GeoJSON (Natural Earth / GADM).
/// 2) Связывает провинции со странами по adm0_a3.
/// 3) Строит граф соседства.
/// 4) Распределяет население стран по провинциям (явный CSV приоритетнее, иначе по площади).
/// 5) Пишет data/cache/world.json.
/// </summary>
public sealed class ImportResult
{
    public WorldFileDto World = new();
    public List<GeoFeature> Features = new();
    public int[] FeatureToProvinceId = System.Array.Empty<int>();
}

public static class WorldImporter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ImportResult Import(string admin0Json, string admin1Json, string? populationCsv = null)
    {
        List<GeoFeature> countryFeatures = GeoJsonParser.Parse(admin0Json);
        List<GeoFeature> provinceFeatures = GeoJsonParser.Parse(admin1Json);

        // Карта: позиция фичи в provinceFeatures -> id провинции в WorldData (-1 = пропущено).
        int[] featureToProvinceId = new int[provinceFeatures.Count];
        System.Array.Fill(featureToProvinceId, -1);

        // --- Страны ---
        var countries = new List<CountryDto>();
        var codeToCountryId = new Dictionary<string, int>(StringComparer.Ordinal);
        var countryPopulation = new Dictionary<int, long>();

        foreach (GeoFeature f in countryFeatures)
        {
            string code = f.Get("ADM0_A3", f.Get("ISO_A3", f.Get("iso_a3"))).Trim();
            if (string.IsNullOrEmpty(code) || code == "-99")
                continue;
            string name = f.Get("NAME", f.Get("name", f.Get("ADMIN", code)));

            var dto = new CountryDto
            {
                Id = countries.Count,
                Code = code,
                NameKey = name,
                FlagId = code.ToLowerInvariant(),
                ColorHex = CountryPalette.ColorFor(code),
                GovernmentType = (int)GovernmentType.Republic,
                Ideology = (int)Ideology.None,
                Population = ParseLong(f.Get("POP_EST")),
                Gdp = ParseDouble(f.Get("GDP_MD_EST")),
            };

            codeToCountryId[code] = dto.Id;
            countryPopulation[dto.Id] = dto.Population;
            countries.Add(dto);
        }

        if (countries.Count == 0)
            throw new InvalidOperationException("WorldImporter: no countries parsed from admin-0 GeoJSON");

        // --- Население по провинциям (опциональный CSV: iso_3166_2 -> pop) ---
        var provincePopByIso = new Dictionary<string, long>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(populationCsv) && File.Exists(populationCsv))
        {
            foreach (Dictionary<string, string> row in CsvTableLoaderFile.LoadFile(populationCsv))
            {
                string iso = CsvTableLoader.Str(row, "iso_3166_2");
                long pop = CsvTableLoader.Long(row, "population");
                if (iso.Length > 0)
                    provincePopByIso[iso] = pop;
            }
        }

        // --- Провинции ---
        var provinces = new List<ProvinceDto>();
        var featureIndexToProvinceId = new Dictionary<int, int>();
        var provinceAreaByCountry = new Dictionary<int, double>();

        foreach (GeoFeature f in provinceFeatures)
        {
            string ownerCode = f.Get("adm0_a3", f.Get("ADM0_A3", f.Get("ISO_A3"))).Trim();
            if (string.IsNullOrEmpty(ownerCode) || ownerCode == "-99")
                continue;

            int provinceId = provinces.Count;
            featureIndexToProvinceId[(int)f.Index] = provinceId;
            featureToProvinceId[(int)f.Index] = provinceId;

            float cx = 0, cy = 0;
            int pointCount = 0;
            foreach (List<Vector2[]> poly in f.Polygons)
                foreach (Vector2[] ring in poly)
                    foreach (Vector2 pt in ring) { cx += pt.X; cy += pt.Y; pointCount++; }
            if (pointCount > 0) { cx /= pointCount; cy /= pointCount; }

            float area = (float)Math.Max(f.AreaKm2, 1.0);

            var dto = new ProvinceDto
            {
                Id = provinceId,
                OwnerCode = ownerCode,
                AreaKm2 = area,
                CentroidX = cx,
                CentroidY = cy,
                Terrain = (int)Terrain.Plains,
                Climate = (int)ClimateFromLatitude(cy),
                ContinentId = ContinentFromCountry(ownerCode),
                TotalPopulation = 0,
            };

            string iso2 = f.Get("iso_3166_2", f.Get("ISO_3166_2"));
            if (provincePopByIso.TryGetValue(iso2, out long pop))
                dto.TotalPopulation = pop;

            if (codeToCountryId.TryGetValue(ownerCode, out int cid))
            {
                provinceAreaByCountry.TryGetValue(cid, out double acc);
                provinceAreaByCountry[cid] = acc + area;
                dto.CoreCodes.Add(ownerCode);
            }

            provinces.Add(dto);
        }

        if (provinces.Count == 0)
            throw new InvalidOperationException("WorldImporter: no provinces parsed from admin-1 GeoJSON");

        // --- Распределение населения стран по провинциям (fallback по площади) ---
        foreach (ProvinceDto p in provinces)
        {
            if (p.TotalPopulation > 0)
                continue;
            if (!codeToCountryId.TryGetValue(p.OwnerCode, out int cid))
                continue;
            if (!countryPopulation.TryGetValue(cid, out long countryPop))
                continue;
            provinceAreaByCountry.TryGetValue(cid, out double countryArea);
            if (countryArea > 0)
                p.TotalPopulation = (long)(countryPop * (p.AreaKm2 / countryArea));
        }

        // --- Соседство ---
        Dictionary<int, List<int>> adjacency = AdjacencyGraph.Build(provinceFeatures);
        foreach (KeyValuePair<int, List<int>> kv in adjacency)
        {
            if (!featureIndexToProvinceId.TryGetValue(kv.Key, out int pid))
                continue;
            var list = new List<int>();
            foreach (int nf in kv.Value)
                if (featureIndexToProvinceId.TryGetValue(nf, out int nid))
                    list.Add(nid);
            list.Sort();
            provinces[pid].NeighborIds = list;
        }

        // --- Столицы: провинция с максимальным населением в стране ---
        var bestPopByCountry = new Dictionary<int, (int pid, long pop)>();
        foreach (ProvinceDto p in provinces)
        {
            if (!codeToCountryId.TryGetValue(p.OwnerCode, out int cid))
                continue;
            if (!bestPopByCountry.TryGetValue(cid, out (int pid, long pop) best) || p.TotalPopulation > best.pop)
                bestPopByCountry[cid] = (p.Id, p.TotalPopulation);
        }
        foreach (KeyValuePair<int, (int pid, long pop)> kv in bestPopByCountry)
            countries[kv.Key].CapitalProvinceId = kv.Value.pid;

        return new ImportResult
        {
            World = new WorldFileDto
            {
                MapWidthPx = GeoProjection.MapWidthPx,
                MapHeightPx = GeoProjection.MapHeightPx,
                Countries = countries,
                Provinces = provinces,
            },
            Features = provinceFeatures,
            FeatureToProvinceId = featureToProvinceId,
        };
    }

    public static void WriteCache(WorldFileDto dto, string path)
    {
        string json = JsonSerializer.Serialize(dto, JsonOptions);
        string dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, json);
    }

    private static Climate ClimateFromLatitude(float mapY)
    {
        // mapY в пикселях; конвертируем в широту.
        float lat = 90f - mapY / GeoProjection.Scale;
        float abs = Mathf.Abs(lat);
        if (abs < 23.5f) return Climate.Tropical;
        if (abs < 35f) return Climate.Arid;
        if (abs < 55f) return Climate.Temperate;
        if (abs < 66.5f) return Climate.Continental;
        return Climate.Polar;
    }

    private static int ContinentFromCountry(string code) => 0; // M3: уточняется по region/subregion

    private static long ParseLong(string s)
    {
        // Значения вида "5300000.0" (Natural Earth) не парсятся long.TryParse — идём через double.
        if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double d))
            return (long)d;
        return 0;
    }

    private static double ParseDouble(string s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0.0;
}

/// <summary>Стабильная палитра цветов стран по ISO-коду (детерминированная).</summary>
public static class CountryPalette
{
    private static readonly string[] Palette =
    {
        "#4E79A7", "#F28E2B", "#E15759", "#76B7B2", "#59A14F",
        "#EDC948", "#B07AA1", "#FF9DA7", "#9C755F", "#BAB0AC",
        "#6B8E23", "#7B68EE", "#CD853F", "#4682B4", "#C71585",
        "#2E8B57", "#8B0000", "#DAA520", "#4169E1", "#A0522D",
    };

    public static string ColorFor(string code)
    {
        int hash = 0;
        foreach (char c in code)
            hash = (hash * 31 + c) & 0x7FFFFFFF;
        return Palette[hash % Palette.Length];
    }
}
