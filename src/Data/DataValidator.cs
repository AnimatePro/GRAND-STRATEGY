using System.Collections.Generic;
using Godot;

namespace GrandStrategy.Data;

/// <summary>
/// Валидация целостности мировых данных после загрузки:
/// непустые массивы, корректные ссылки (owner/controller/capital/neighbors),
/// симметричность графа соседства, отсутствие битых id.
/// Результат агрегируется и возвращается; ошибки уходят в лог и EventBus.
/// </summary>
public static class DataValidator
{
    public sealed class Result
    {
        public List<string> Errors = new();
        public List<string> Warnings = new();
        public bool Ok => Errors.Count == 0;
    }

    public static Result Validate(WorldData world)
    {
        var result = new Result();

        if (world.Provinces.Length == 0)
            result.Errors.Add("No provinces loaded");

        if (world.Countries.Length == 0)
            result.Errors.Add("No countries loaded");

        // Симметричность и валидность соседства.
        for (int i = 0; i < world.Provinces.Length; i++)
        {
            ProvinceData p = world.Provinces[i];
            if (p.Id != i)
                result.Errors.Add($"Province index/id mismatch at {i} (id={p.Id})");

            foreach (int nid in p.NeighborIds)
            {
                if (nid < 0 || nid >= world.Provinces.Length)
                {
                    result.Errors.Add($"Province {p.Id} has invalid neighbor {nid}");
                    continue;
                }
                // Обратная ссылка должна существовать.
                ProvinceData n = world.Provinces[nid];
                if (System.Array.IndexOf(n.NeighborIds, p.Id) < 0)
                    result.Warnings.Add($"Neighbor asymmetry: {p.Id} -> {nid}");
            }
        }

        // Валидность ссылок стран.
        for (int i = 0; i < world.Countries.Length; i++)
        {
            CountryData c = world.Countries[i];
            if (c == null)
            {
                result.Errors.Add($"Country slot {i} is null");
                continue;
            }
            if (c.Id != i)
                result.Errors.Add($"Country index/id mismatch at {i}");

            if (c.CapitalProvinceId >= 0 && !world.ProvinceIdToIndex.ContainsKey(c.CapitalProvinceId))
                result.Errors.Add($"Country {c.Code} capital province {c.CapitalProvinceId} not found");

            foreach (int pid in c.OwnedProvinceIds)
            {
                if (!world.ProvinceIdToIndex.ContainsKey(pid))
                {
                    result.Errors.Add($"Country {c.Code} owns missing province {pid}");
                    break;
                }
            }
        }

        // Проверка владельцев провинций.
        for (int i = 0; i < world.Provinces.Length; i++)
        {
            ProvinceData p = world.Provinces[i];
            if (p.OwnerId >= 0 && (p.OwnerId >= world.Countries.Length || world.Countries[p.OwnerId] == null))
                result.Errors.Add($"Province {p.Id} has invalid owner {p.OwnerId}");
            if (p.ControllerId >= 0 && (p.ControllerId >= world.Countries.Length || world.Countries[p.ControllerId] == null))
                result.Errors.Add($"Province {p.Id} has invalid controller {p.ControllerId}");
        }

        return result;
    }
}
