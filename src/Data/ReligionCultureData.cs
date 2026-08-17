namespace GrandStrategy.Data;

/// <summary>Религия (data/religions.csv).</summary>
public struct ReligionData
{
    public ReligionData() { } // CS8983
    public int Id;
    public string NameKey = string.Empty;
    public string Group = string.Empty; // Abrahamic/Dharmic/Traditional/...
}

/// <summary>Культура/этнолингвистическая группа (data/cultures.csv).</summary>
public struct CultureData
{
    public CultureData() { } // CS8983
    public int Id;
    public string NameKey = string.Empty;
    public string Family = string.Empty; // Germanic/Romance/Slavic/...
}
