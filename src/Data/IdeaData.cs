namespace GrandStrategy.Data;

/// <summary>Национальная идея (бонус за очки наследия).</summary>
public struct IdeaData
{
    public IdeaData() { } // CS8983

    public int Id;
    public string NameKey = string.Empty;
    public double Cost;          // стоимость в очках наследия
    public double TaxMult = 1.0; // множитель налогов
    public double MilitaryMult = 1.0; // множитель армии
    public double StabilityBonus = 0.0; // + стабильность
    public double ResearchMult = 1.0; // множитель исследований
}
