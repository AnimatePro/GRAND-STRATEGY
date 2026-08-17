using System.Collections.Generic;
using Godot;

namespace GrandStrategy.Data;

/// <summary>
/// Построение графа соседства провинций по общей границе.
/// Две провинции считаются соседями, если у них есть общее ребро (2+ общих точки),
/// с учётом того, что одинаковые ребра могут идти в противоположных направлениях.
/// Сложность O(E) по всем рёбрам. Результат — массив соседей на провинцию.
/// </summary>
public static class AdjacencyGraph
{
    public static Dictionary<int, List<int>> Build(List<GeoFeature> features)
    {
        // edge key -> список провинций, владеющих этим ребром.
        var edgeOwners = new Dictionary<EdgeKey, List<int>>();

        for (int f = 0; f < features.Count; f++)
        {
            GeoFeature feature = features[f];
            foreach (List<Vector2[]> polygon in feature.Polygons)
            {
                foreach (Vector2[] ring in polygon)
                {
                    for (int i = 0; i < ring.Length; i++)
                    {
                        Vector2 a = ring[i];
                        Vector2 b = ring[(i + 1) % ring.Length];
                        var key = new EdgeKey(a, b);
                        if (!edgeOwners.TryGetValue(key, out List<int>? owners))
                        {
                            owners = new List<int>();
                            edgeOwners[key] = owners;
                        }
                        if (!owners.Contains(f))
                            owners.Add(f);
                    }
                }
            }
        }

        // Собираем пары соседей по общим рёбрам.
        var neighbors = new Dictionary<int, HashSet<int>>();
        for (int f = 0; f < features.Count; f++)
            neighbors[f] = new HashSet<int>();

        foreach (KeyValuePair<EdgeKey, List<int>> kv in edgeOwners)
        {
            List<int> owners = kv.Value;
            for (int i = 0; i < owners.Count; i++)
            {
                for (int j = i + 1; j < owners.Count; j++)
                {
                    neighbors[owners[i]].Add(owners[j]);
                    neighbors[owners[j]].Add(owners[i]);
                }
            }
        }

        var result = new Dictionary<int, List<int>>();
        foreach (KeyValuePair<int, HashSet<int>> kv in neighbors)
        {
            var list = new List<int>(kv.Value);
            list.Sort();
            result[kv.Key] = list;
        }
        return result;
    }

    /// <summary>Ключ ненаправленного ребра (точки упорядочены лексикографически).</summary>
    private readonly struct EdgeKey : System.IEquatable<EdgeKey>
    {
        private readonly float _x1, _y1, _x2, _y2;

        public EdgeKey(Vector2 a, Vector2 b)
        {
            if (a.X < b.X || (a.X == b.X && a.Y <= b.Y))
            {
                _x1 = a.X; _y1 = a.Y; _x2 = b.X; _y2 = b.Y;
            }
            else
            {
                _x1 = b.X; _y1 = b.Y; _x2 = a.X; _y2 = a.Y;
            }
        }

        public bool Equals(EdgeKey other) =>
            _x1 == other._x1 && _y1 == other._y1 && _x2 == other._x2 && _y2 == other._y2;

        public override bool Equals(object? obj) => obj is EdgeKey other && Equals(other);

        public override int GetHashCode() =>
            System.HashCode.Combine(_x1, _y1, _x2, _y2);
    }
}
