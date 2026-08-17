using Godot;

namespace GrandStrategy.Data;

/// <summary>
/// Горячие данные провинции (struct для кэш-локальности, хранится в ProvinceData[]).
/// 8 демографических групп: дети(0-10), подростки(11-17), взрослые(18-59), пожилые(60+),
/// с разбивкой по полу. Мутации происходят по индексу массива (provinces[i].OwnerId = x).
/// Массивы ссылочные — разделяются между копиями struct, поэтому создаются через Create().
/// </summary>
public struct ProvinceData
{
    public int Id;
    public int OwnerId;      // страна-владелец, -1 = не заселено
    public int ControllerId; // страна-оккупант, -1 = контролирует владелец
    public int RegionId;     // регион (субъект/штат/область)
    public int ContinentId;
    public bool IsCoastal;   // имеет выход к морю (для морской переброски армий)
    public Terrain Terrain;
    public Climate Climate;
    public float AreaKm2;

    // 8 демографических групп (человек)
    public int MaleChildren;
    public int FemaleChildren;
    public int MaleTeens;
    public int FemaleTeens;
    public int MaleAdults;
    public int FemaleAdults;
    public int MaleSeniors;
    public int FemaleSeniors;

    public float Infrastructure; // 0..1
    public float Development;    // 0..1
    public float TaxBase;        // денежная база налогообложения
    public float Unrest;         // 0..1
    public float Autonomy;       // 0..1 (автономия от центра)

    public int[] CoreIds;      // страны, имеющие ядро на провинцию
    public int[] ResourceIds;  // id ресурсов (см. GoodData)
    public int[] BuildingIds;  // id построек
    public int[] NeighborIds;  // id соседних провинций (граф смежности)

    public Vector2 Centroid;   // центроид в проекционных мировых координатах (пиксели карты)
    public int LodLevel;

    // --- Производные характеристики (только чтение) ------------------------

    public readonly int TotalPopulation =>
        MaleChildren + FemaleChildren +
        MaleTeens + FemaleTeens +
        MaleAdults + FemaleAdults +
        MaleSeniors + FemaleSeniors;

    public readonly int WorkingAgePopulation => MaleAdults + FemaleAdults;

    /// <summary>Взрослые мужчины — источник мобилизации/рекрутов.</summary>
    public readonly int MaleAdultCount => MaleAdults;

    public readonly int ChildrenCount => MaleChildren + FemaleChildren;
    public readonly int SeniorsCount => MaleSeniors + FemaleSeniors;

    /// <summary>Коэффициент иждивенческой нагрузки (дети+пожилые на взрослого).</summary>
    public readonly float DependencyRatio =>
        WorkingAgePopulation > 0
            ? (float)(ChildrenCount + SeniorsCount) / WorkingAgePopulation
            : 0f;

    /// <summary>Действительный владелец для логики контроля (оккупация переопределяет).</summary>
    public readonly int EffectiveOwnerId => ControllerId >= 0 ? ControllerId : OwnerId;

    public static ProvinceData Create(int id)
    {
        return new ProvinceData
        {
            Id = id,
            OwnerId = -1,
            ControllerId = -1,
            RegionId = -1,
            ContinentId = -1,
            CoreIds = System.Array.Empty<int>(),
            ResourceIds = System.Array.Empty<int>(),
            BuildingIds = System.Array.Empty<int>(),
            NeighborIds = System.Array.Empty<int>(),
        };
    }
}
