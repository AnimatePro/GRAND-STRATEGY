namespace GrandStrategy.Data;

/// <summary>
/// Здание/сооружение в провинции. Строится за деньги, даёт модификаторы провинции
/// (производство, инфраструктура, оборона) и/или страны.
/// </summary>
public struct BuildingData
{
    public BuildingData() { } // CS8983

    public int Id;
    public string NameKey = string.Empty;
    public BuildingCategory Category;
    public string IconPath = string.Empty;  // спрайт здания (res://assets/buildings/*.png)
    public double BuildCost;      // стоимость строительства
    public double Upkeep;         // содержание за ход
    public int BuildTurns;        // время постройки (в ходах)
    public double DevelopmentBonus;    // +к развитию провинции (производство)
    public double InfrastructureBonus; // +к инфраструктуре провинции
    public int MaxPerProvince = 1;
    public bool RequiresCoast;

    public static BuildingData Create(int id, string nameKey, BuildingCategory category)
    {
        return new BuildingData { Id = id, NameKey = nameKey, Category = category };
    }
}
