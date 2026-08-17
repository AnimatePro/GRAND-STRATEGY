using System.Collections.Generic;

namespace GrandStrategy.Data;

// DTO для кэшированного файла мира (data/cache/world.json).
// Провинции хранятся без полигональной геометрии (геометрия нужна только импортёру);
// в рантайме используются: центроид (метки/миникарта), соседи (перемещение), owner (раскраска).

public sealed class WorldFileDto
{
    public int Version = 1;
    public float MapWidthPx;
    public float MapHeightPx;
    public List<CountryDto> Countries = new();
    public List<ProvinceDto> Provinces = new();
}

public sealed class CountryDto
{
    public int Id;
    public string Code = string.Empty;
    public string NameKey = string.Empty;
    public string NameRu = string.Empty;
    public string FlagId = string.Empty;
    public string ColorHex = "#888888";
    public int GovernmentType;
    public int Ideology;
    public int CapitalProvinceId = -1;
    public long Population;
    public double Gdp;
    public float Literacy = 0.5f;
    public float Urbanization = 0.5f;
    public double Treasury = 1000.0;
    public int ReligionId;
    public int CultureId;
}

public sealed class ProvinceDto
{
    public int Id;
    public string OwnerCode = string.Empty;
    public string NameEn = string.Empty;
    public string NameRu = string.Empty;
    public int RegionId = -1;
    public int ContinentId = -1;
    public bool IsCoastal;
    public int Terrain;
    public int Climate;
    public float AreaKm2;
    public float Development;
    public float Infrastructure;
    public float CentroidX;
    public float CentroidY;
    public long TotalPopulation;
    public int ReligionId;
    public int CultureId;
    public List<int> NeighborIds = new();
    public List<int> ResourceIds = new();
    public List<string> CoreCodes = new();
}
