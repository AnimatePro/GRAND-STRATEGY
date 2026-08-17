using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace GrandStrategy.Data;

/// <summary>
/// Разобранный гео-объект: список колец полигонов (в проекционных мировых координатах)
/// и произвольные свойства из feature.properties.
/// </summary>
public sealed class GeoFeature
{
    public long Index;
    /// <summary>Полигоны; каждый — список колец; каждое кольцо — список точек (замкнутое).</summary>
    public List<List<Vector2[]>> Polygons = new();
    public Dictionary<string, string> Properties = new();
    /// <summary>Приближённая площадь в км² (по широте/долготе).</summary>
    public double AreaKm2;

    public string Get(string key, string fallback = "")
    {
        return Properties.TryGetValue(key, out string? v) && !string.IsNullOrEmpty(v) ? v : fallback;
    }
}

/// <summary>
/// Проекция lon/lat (градусы) в плоские координаты карты (пиксели).
/// Равнопромежуточная (Equirectangular) — как в AoH3. Запад/север -> 0.
/// </summary>
public static class GeoProjection
{
    public const float Scale = 16f; // пикселей на градус (совпадает с tools/import_world.py)

    public static Vector2 ToMap(float lon, float lat)
    {
        float x = (lon + 180f) * Scale;
        float y = (90f - lat) * Scale;
        return new Vector2(x, y);
    }

    public static float MapWidthPx => 360f * Scale;
    public static float MapHeightPx => 180f * Scale;
}

/// <summary>
/// Парсер GeoJSON (FeatureCollection с Polygon/MultiPolygon).
/// Работает на System.Text.Json без внешних зависимостей. Возвращает список GeoFeature
/// с координатами, спроецированными в пиксели карты.
/// </summary>
public static class GeoJsonParser
{
    public static List<GeoFeature> Parse(string json)
    {
        var features = new List<GeoFeature>();
        using JsonDocument doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("type", out JsonElement typeEl) ||
            typeEl.GetString() != "FeatureCollection")
        {
            // Возможно, это одиночный Feature — обрабатываем как коллекцию из одного.
            if (doc.RootElement.TryGetProperty("type", out JsonElement t2) && t2.GetString() == "Feature")
            {
                features.Add(ParseFeature(doc.RootElement, 0));
                return features;
            }
            throw new InvalidOperationException("GeoJSON: expected FeatureCollection");
        }

        if (!doc.RootElement.TryGetProperty("features", out JsonElement list))
            throw new InvalidOperationException("GeoJSON: no 'features' array");

        long i = 0;
        foreach (JsonElement feature in list.EnumerateArray())
        {
            features.Add(ParseFeature(feature, i++));
        }

        return features;
    }

    private static GeoFeature ParseFeature(JsonElement feature, long index)
    {
        var result = new GeoFeature { Index = index };

        if (feature.TryGetProperty("properties", out JsonElement props) && props.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in props.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                    result.Properties[p.Name] = p.Value.GetString() ?? string.Empty;
                else if (p.Value.ValueKind == JsonValueKind.Number)
                    result.Properties[p.Name] = p.Value.GetRawText();
            }
        }

        if (!feature.TryGetProperty("geometry", out JsonElement geom) || geom.ValueKind != JsonValueKind.Object)
            return result; // Feature без геометрии (напр. страна-точка)

        string? geomType = geom.TryGetProperty("type", out JsonElement gt) ? gt.GetString() : null;
        if (!geom.TryGetProperty("coordinates", out JsonElement coords))
            return result;

        switch (geomType)
        {
            case "Polygon":
                result.Polygons.Add(ParsePolygon(coords));
                break;
            case "MultiPolygon":
                foreach (JsonElement poly in coords.EnumerateArray())
                    result.Polygons.Add(ParsePolygon(poly));
                break;
            default:
                // GeometryCollection/Point/LineString не являются полигонами провинций — пропускаем.
                break;
        }

        result.AreaKm2 = ComputeAreaKm2(result.Polygons);
        return result;
    }

    private static List<Vector2[]> ParsePolygon(JsonElement polygon)
    {
        var rings = new List<Vector2[]>();
        foreach (JsonElement ring in polygon.EnumerateArray())
        {
            var points = new List<Vector2>();
            foreach (JsonElement pt in ring.EnumerateArray())
            {
                // Порядок в GeoJSON: [lon, lat]
                float lon = pt[0].GetSingle();
                float lat = pt[1].GetSingle();
                points.Add(GeoProjection.ToMap(lon, lat));
            }
            rings.Add(points.ToArray());
        }
        return rings;
    }

    /// <summary>Грубая оценка площади в км² по проекционным полигонам (без сферической коррекции).</summary>
    private static double ComputeAreaKm2(List<List<Vector2[]>> polygons)
    {
        double areaPx = 0.0;
        foreach (List<Vector2[]> polygon in polygons)
        {
            foreach (Vector2[] ring in polygon)
            {
                double signed = Shoelace(ring);
                // Внешние кольца против часовой -> положительные, дырки -> отрицательные.
                areaPx += Math.Abs(signed) * (signed >= 0 ? 1 : -1);
            }
        }
        // 1 градус² ≈ 111.32² км²; Scale пикселей/градус.
        double pxPerDeg2 = GeoProjection.Scale * GeoProjection.Scale;
        return areaPx / pxPerDeg2 * (111.32 * 111.32);
    }

    private static double Shoelace(Vector2[] ring)
    {
        double sum = 0.0;
        for (int i = 0; i < ring.Length; i++)
        {
            Vector2 a = ring[i];
            Vector2 b = ring[(i + 1) % ring.Length];
            sum += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        return sum / 2.0;
    }
}
