namespace GrandStrategy.Data;

/// <summary>Закон (политика), принимается страной, даёт модификаторы.</summary>
public struct LawData
{
    public LawData() { } // CS8983

    public int Id;
    public string NameKey = string.Empty;
    public LawCategory Category;
    public double PoliticalCost;    // политическая стоимость принятия
    public double UpkeepPerTurn;

    // Конкретные эффекты (множители / прибавки).
    public double TaxMult = 1.0;        // множитель налоговых поступлений
    public double ConscriptionRate = 0.0; // доля населения в резерве (призыв)
    public double StabilityBonus = 0.0;   // + к стабильности
    public double MilitaryCostMult = 1.0; // множитель стоимости армии
    public double ResearchMult = 1.0;     // множитель исследований
    public double TradeMult = 1.0;        // множитель торговли

    public static LawData Create(int id, string nameKey, LawCategory category)
    {
        return new LawData { Id = id, NameKey = nameKey, Category = category };
    }
}
