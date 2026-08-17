namespace GrandStrategy.Systems.Economy;

/// <summary>Fallback-константы экономики (реальный баланс — в data/*.csv/json).</summary>
public static class EconomyConstants
{
    public const double FoodPerCapita = 1.0;        // ед. продовольствия на человека в год
    public const double RuralFoodPerWorker = 3.0;   // ед. на сельского работника
    public const double IndustryProductivity = 2.5; // ед. на индустриального работника
    public const double BaseExtraction = 400.0;     // ед. ресурса на провинцию-источник

    public const double WageShare = 0.55;           // доля труда в добавленной стоимости

    public const double MinPriceMult = 0.1;         // границы цены
    public const double MaxPriceMult = 10.0;

    // Инфляция/долг
    public const double MoneyPrintingInflationFactor = 0.02;
    public const double DemandInflationFactor = 0.005;
    public const double DefaultDebtToGdp = 1.5;     // порог дефолта

    public const double BaseInterestRate = 0.04;
}
