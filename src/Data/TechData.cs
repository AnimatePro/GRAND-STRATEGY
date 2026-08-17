namespace GrandStrategy.Data;

public enum TechCategory
{
    Economy = 0,
    Military = 1,
    Society = 2,
}

/// <summary>Технология (data-driven, data/techs.csv). Эффекты — аддитивные множители.</summary>
public struct TechData
{
    public TechData() { } // CS8983

    public int Id;
    public string NameKey = string.Empty;
    public TechCategory Category;
    public double Cost;          // стоимость исследования (очки)
    public double FoodMult;      // +продовольствие
    public double ProductionMult;// +промышленность
    public double MilitaryMult;  // +атака/оборона
    public double TaxMult;       // +налоги
    public double ResearchMult;  // +скорость исследований
}

/// <summary>Агрегированные эффекты технологий страны (мультипликаторы, 1.0 = базово).</summary>
public struct TechEffects
{
    public double Food;
    public double Production;
    public double Military;
    public double Tax;
    public double Research;

    public static TechEffects operator +(TechEffects a, TechEffects b) => new()
    {
        Food = a.Food + b.Food,
        Production = a.Production + b.Production,
        Military = a.Military + b.Military,
        Tax = a.Tax + b.Tax,
        Research = a.Research + b.Research,
    };

    public static TechEffects From(TechData t) => new()
    {
        Food = t.FoodMult,
        Production = t.ProductionMult,
        Military = t.MilitaryMult,
        Tax = t.TaxMult,
        Research = t.ResearchMult,
    };
}

/// <summary>Формируемая нация (data/formables.json).</summary>
public sealed class FormableData
{
    public string Id { get; set; } = string.Empty;
    public string NameKey { get; set; } = string.Empty;
    public List<string> RequiresCodes { get; set; } = new(); // столицы каких стран нужно контролировать
    public string ColorHex { get; set; } = "#888888";
}
