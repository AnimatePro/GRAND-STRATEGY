using System;
using System.IO;
using Godot;
using GrandStrategy.Data;

namespace GrandStrategy.Map;

/// <summary>
/// Редакторный инструмент импорта мира (запускается один раз).
/// Парсит admin-0/admin-1 GeoJSON (Natural Earth / GADM), строит WorldData,
/// растеризует ID-карту и маску границ, пишет data/cache/world.json + PNG.
/// Переключатель RunImport в инспекторе запускает пайплайн.
/// </summary>
#if TOOLS
[Tool]
#endif
public partial class MapImporterTool : Node
{
    [Export] public string Admin0Path = "res://data/source/ne_10m_admin_0_countries.geojson";
    [Export] public string Admin1Path = "res://data/source/ne_10m_admin_1_states_provinces.geojson";
    [Export] public string PopulationCsvPath = ""; // опционально: iso_3166_2 -> population

    [Export]
    public bool RunImport
    {
        get => false;
        set
        {
            if (value)
                Import();
        }
    }

    private void Import()
    {
        GD.Print("MapImporterTool: starting import...");
        try
        {
            string admin0 = ReadSource(Admin0Path);
            string admin1 = ReadSource(Admin1Path);

            // 1. Модель данных (+ сопоставление фича->провинция).
            ImportResult result = WorldImporter.Import(admin0, admin1,
                string.IsNullOrEmpty(PopulationCsvPath) ? null : ProjectSettings.GlobalizePath(PopulationCsvPath));

            // 2. Растеризация ID-карты и маски границ.
            int width = (int)GeoProjection.MapWidthPx;
            int height = (int)GeoProjection.MapHeightPx;

            Image idImage = Image.CreateEmpty(width, height, false, Image.Format.Rgb8);
            idImage.Fill(Colors.Black); // океан = 0
            PolygonRasterizer.Rasterize(idImage, result.Features, result.FeatureToProvinceId, width, height);
            Image borderMask = PolygonRasterizer.BuildBorderMask(idImage, width, height);

            // 3. Запись файлов.
            string cacheDir = ProjectSettings.GlobalizePath("res://data/cache");
            Directory.CreateDirectory(cacheDir);
            WorldImporter.WriteCache(result.World, Path.Combine(cacheDir, "world.json"));
            idImage.SavePng(Path.Combine(cacheDir, "id_map.png"));
            borderMask.SavePng(Path.Combine(cacheDir, "border_mask.png"));

            GD.Print($"MapImporterTool: done — {result.World.Provinces.Count} provinces, {result.World.Countries.Count} countries, {width}x{height} px");
        }
        catch (Exception ex)
        {
            GD.PushError($"MapImporterTool failed: {ex.Message}");
        }
    }

    private static string ReadSource(string resPath)
    {
        string abs = ProjectSettings.GlobalizePath(resPath);
        if (!File.Exists(abs))
            throw new FileNotFoundException($"Source not found: {resPath} (place GeoJSON there first)");
        return File.ReadAllText(abs);
    }
}
