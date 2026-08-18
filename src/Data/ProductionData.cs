namespace GrandStrategy.Data;

/// <summary>Реальная добыча ресурсов страны (data/production.csv).</summary>
public struct ProductionData
{
    public ProductionData() { } // CS8983

    public double OilKbd;    // нефть, тысяч баррелей в день
    public double GasBcm;    // газ, млрд кубометров в год
    public double CoalMt;    // уголь, млн тонн в год
    public double IronMt;    // железная руда, млн тонн в год
}
