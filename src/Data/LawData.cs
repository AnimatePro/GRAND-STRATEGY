namespace GrandStrategy.Data;

/// <summary>Закон (политика), принимается страной, даёт модификаторы.</summary>
public struct LawData
{
    public LawData() { } // CS8983

    public int Id;
    public string NameKey = string.Empty;
    public LawCategory Category;
    public double[] Effects;        // эффекты закона (ключи модификаторов см. ModifierData)
    public double PoliticalCost;    // политическая стоимость принятия
    public double UpkeepPerTurn;

    public static LawData Create(int id, string nameKey, LawCategory category)
    {
        return new LawData { Id = id, NameKey = nameKey, Category = category };
    }
}
