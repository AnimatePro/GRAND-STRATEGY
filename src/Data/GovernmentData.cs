namespace GrandStrategy.Data;

/// <summary>Форма правления (государственный строй) с базовыми модификаторами страны.</summary>
public struct GovernmentData
{
    public GovernmentData() { } // CS8983

    public int Id;
    public GovernmentType Type;
    public string NameKey = string.Empty;
    public double TaxEfficiencyMult = 1.0;   // эффективность сбора налогов
    public double LegitimacyBonus = 0.0;
    public double StabilityBonus = 0.0;
    public double MilitaryCostMult = 1.0;
    public double UnrestReduction = 0.0;

    public static GovernmentData Create(int id, GovernmentType type, string nameKey)
    {
        return new GovernmentData { Id = id, Type = type, NameKey = nameKey };
    }
}
