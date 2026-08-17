using System.Collections.Generic;
using Godot;
using GrandStrategy.Data;

namespace GrandStrategy.Map;

/// <summary>
/// CPU-растеризатор полигонов провинций в ID-карту (редакторное время, однократно).
/// Заливка по правилу even-odd (обрабатывает дырки) + кодирование id в RGB.
/// Результат: Image формата Rgb8 размером (ширина, высота) карты в пикселях.
/// </summary>
public static class PolygonRasterizer
{
    /// <summary>Кодирует индекс провинции (0-based) в цвет; 0 резервируется под океан.</summary>
    public static Color EncodeId(int provinceIndex)
    {
        int id = provinceIndex + 1;
        int r = id & 0xFF;
        int g = (id >> 8) & 0xFF;
        int b = (id >> 16) & 0xFF;
        return new Color(r / 255f, g / 255f, b / 255f);
    }

    /// <summary>
    /// Заливает все полигоны гео-объектов в изображение (even-odd).
    /// featureToProvinceId сопоставляет позицию фичи с id провинции в WorldData (-1 = пропуск).
    /// </summary>
    public static void Rasterize(Image image, List<GeoFeature> features, int[] featureToProvinceId, int width, int height)
    {
        for (int f = 0; f < features.Count; f++)
        {
            int pid = featureToProvinceId[f];
            if (pid < 0)
                continue;
            Color idColor = EncodeId(pid);
            foreach (List<Vector2[]> polygon in features[f].Polygons)
                FillEvenOdd(image, polygon, idColor, width, height);
        }
    }

    /// <summary>
    /// Сканлайновая заливка even-odd. Собирает пересечения всех колец полигона
    /// на каждой строке и закрашивает между парами — дырки учитываются автоматически.
    /// </summary>
    private static void FillEvenOdd(Image image, List<Vector2[]> rings, Color color, int width, int height)
    {
        // Ограничивающий прямоугольник полигона.
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (Vector2[] ring in rings)
            foreach (Vector2 p in ring)
            {
                minY = Mathf.Min(minY, p.Y);
                maxY = Mathf.Max(maxY, p.Y);
            }
        int y0 = Mathf.Clamp((int)Mathf.Floor(minY), 0, height - 1);
        int y1 = Mathf.Clamp((int)Mathf.Ceil(maxY), 0, height - 1);

        var xs = new List<float>();
        for (int y = y0; y <= y1; y++)
        {
            float fy = y + 0.5f;
            xs.Clear();
            foreach (Vector2[] ring in rings)
            {
                for (int i = 0; i < ring.Length; i++)
                {
                    Vector2 a = ring[i];
                    Vector2 b = ring[(i + 1) % ring.Length];
                    if ((a.Y <= fy && b.Y > fy) || (b.Y <= fy && a.Y > fy))
                    {
                        float t = (fy - a.Y) / (b.Y - a.Y);
                        xs.Add(a.X + t * (b.X - a.X));
                    }
                }
            }
            xs.Sort();
            for (int i = 0; i + 1 < xs.Count; i += 2)
            {
                int xa = Mathf.Clamp((int)Mathf.Floor(xs[i]), 0, width - 1);
                int xb = Mathf.Clamp((int)Mathf.Ceil(xs[i + 1]), 0, width - 1);
                for (int x = xa; x <= xb; x++)
                    image.SetPixel(x, y, color);
            }
        }
    }

    /// <summary>
    /// Генерирует маску границ из ID-карты: пиксель является границей,
    /// если сосед справа или снизу имеет другой id (включая океан id=0 -> береговая линия).
    /// </summary>
    public static Image BuildBorderMask(Image idImage, int width, int height)
    {
        Image mask = Image.CreateEmpty(width, height, false, Image.Format.Rgb8);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color c = idImage.GetPixel(x, y);
                bool border = false;
                if (x + 1 < width && idImage.GetPixel(x + 1, y) != c) border = true;
                else if (y + 1 < height && idImage.GetPixel(x, y + 1) != c) border = true;
                else if (x == width - 1 || y == height - 1) border = true;
                mask.SetPixel(x, y, border ? Colors.White : Colors.Black);
            }
        }
        return mask;
    }
}
