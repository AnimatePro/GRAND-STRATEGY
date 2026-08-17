using System.Collections.Generic;
using GrandStrategy.Utils;

namespace GrandStrategy.Data;

/// <summary>
/// Детерминированное распределение природных ресурсов по провинциям на основе
/// ландшафта/климата (id товаров см. data/goods.csv). Может быть переопределено
/// реальным датасетом ресурсов (CSV: province_id -> goods) — тогда WorldDataLoader
/// подставит явные значения, а этот fallback не сработает.
/// </summary>
public static class ResourceAssigner
{
    public const int Timber = 2;
    public const int Iron = 3;
    public const int Coal = 4;
    public const int Oil = 5;
    public const int Gas = 6;
    public const int Gold = 13;
    public const int RareMetals = 14;

    public static int[] Assign(int provinceId, Terrain terrain, Climate climate)
    {
        var rng = new Rng(provinceId * 0x9E3779B97F4A7C15L + 0x1234567);
        var result = new List<int>(2);

        switch (terrain)
        {
            case Terrain.Forest:
                if (rng.Chance(0.8)) result.Add(Timber);
                if (rng.Chance(0.15)) result.Add(Coal);
                break;
            case Terrain.Jungle:
                if (rng.Chance(0.9)) result.Add(Timber);
                if (rng.Chance(0.1)) result.Add(Oil);
                break;
            case Terrain.Desert:
                if (rng.Chance(0.35)) result.Add(Oil);
                if (rng.Chance(0.2)) result.Add(Gas);
                if (rng.Chance(0.1)) result.Add(Gold);
                break;
            case Terrain.Mountains:
                if (rng.Chance(0.4)) result.Add(Iron);
                if (rng.Chance(0.3)) result.Add(Coal);
                if (rng.Chance(0.2)) result.Add(RareMetals);
                if (rng.Chance(0.1)) result.Add(Gold);
                break;
            case Terrain.Hills:
                if (rng.Chance(0.35)) result.Add(Iron);
                if (rng.Chance(0.3)) result.Add(Coal);
                if (rng.Chance(0.1)) result.Add(RareMetals);
                break;
            default:
                if (climate == Climate.Continental || climate == Climate.Polar)
                    if (rng.Chance(0.25)) result.Add(Gas);
                if (rng.Chance(0.1)) result.Add(Coal);
                if (rng.Chance(0.08)) result.Add(Iron);
                break;
        }

        // Редкий бонусный ресурс.
        if (result.Count == 0 && rng.Chance(0.05))
            result.Add(Coal);

        return result.ToArray();
    }
}
