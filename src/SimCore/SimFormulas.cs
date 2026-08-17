using System;

namespace GrandStrategy.SimCore;

/// <summary>
/// Чистые игровые формулы БЕЗ зависимостей от Godot (только System).
/// Единственный источник истины: Godot-менеджеры (PopulationSystem, EconomyManager,
/// MilitaryManager, TradeManager) вызывают эти методы; xUnit-тесты (tests/) и
/// Python-стенд (tools/sim_harness.py) проверяют их напрямую.
/// Все методы детерминированы, без аллокаций в горячем пути, без NaN/Infinity.
/// </summary>
public static class SimFormulas
{
    // ===================== ДЕМОГРАФИЯ =====================

    // 0.056 рождений/женщина/год ≈ TFR 2.0 (замещение при смертности ~1.6%/год).
    public const double BirthRate = 0.056;
    public const double ChildMortality = 0.006;
    public const double TeenMortality = 0.0015;
    public const double AdultMortality = 0.007;
    public const double SeniorMortality = 0.06;

    public const double ChildrenYears = 11.0; // 0-10
    public const double TeenYears = 7.0;      // 11-17
    public const double AdultYears = 42.0;    // 18-59

    public const double ParticipationRate = 0.56;

    /// <summary>Годовой демографический переход 8 групп (рождения/смертность/старение).</summary>
    public static void TickDemographics(
        ref int maleChildren, ref int femaleChildren,
        ref int maleTeens, ref int femaleTeens,
        ref int maleAdults, ref int femaleAdults,
        ref int maleSeniors, ref int femaleSeniors)
    {
        long births = (long)(femaleAdults * BirthRate);
        int maleBirths = (int)(births * 0.51);
        int femaleBirths = (int)(births - maleBirths);

        long mcD = (long)(maleChildren * ChildMortality);
        long fcD = (long)(femaleChildren * ChildMortality);
        long mtD = (long)(maleTeens * TeenMortality);
        long ftD = (long)(femaleTeens * TeenMortality);
        long maD = (long)(maleAdults * AdultMortality);
        long faD = (long)(femaleAdults * AdultMortality);
        long msD = (long)(maleSeniors * SeniorMortality);
        long fsD = (long)(femaleSeniors * SeniorMortality);

        long mC2T = (long)(maleChildren / ChildrenYears);
        long fC2T = (long)(femaleChildren / ChildrenYears);
        long mT2A = (long)(maleTeens / TeenYears);
        long fT2A = (long)(femaleTeens / TeenYears);
        long mA2S = (long)(maleAdults / AdultYears);
        long fA2S = (long)(femaleAdults / AdultYears);

        maleChildren = ClampInt(maleChildren - mcD - mC2T + maleBirths);
        femaleChildren = ClampInt(femaleChildren - fcD - fC2T + femaleBirths);
        maleTeens = ClampInt(maleTeens - mtD - mT2A + mC2T);
        femaleTeens = ClampInt(femaleTeens - ftD - fT2A + fC2T);
        maleAdults = ClampInt(maleAdults - maD - mA2S + mT2A);
        femaleAdults = ClampInt(femaleAdults - faD - fA2S + fT2A);
        maleSeniors = ClampInt(maleSeniors - msD + mA2S);
        femaleSeniors = ClampInt(femaleSeniors - fsD + fA2S);
    }

    /// <summary>Рабочая сила из 8 групп (взрослые + частично подростки/пожилые).</summary>
    public static double LaborForce(int mc, int fc, int mt, int ft, int ma, int fa, int ms, int fs) =>
        (ma + fa) * ParticipationRate + (mt + ft) * 0.15 + (ms + fs) * 0.05;

    // ===================== ЭКОНОМИКА =====================

    public const double MinPriceMult = 0.1;
    public const double MaxPriceMult = 10.0;
    public const double WageShare = 0.55;
    public const double MoneyPrintingInflationFactor = 0.02;
    public const double DemandInflationFactor = 0.005;
    public const double DefaultDebtToGdp = 1.5;
    public const double BaseInterestRate = 0.04;

    /// <summary>Цена товара: base * (спрос/предложение)^эластичность, кламп [0.1x, 10x].</summary>
    public static double PriceFor(double basePrice, double supply, double demand, double elasticity)
    {
        double s = Math.Max(supply, 1.0);
        double factor = Math.Pow(demand / s, elasticity);
        return Math.Clamp(basePrice * factor, basePrice * MinPriceMult, basePrice * MaxPriceMult);
    }

    /// <summary>Инфляционный вклад денежной эмиссии (финансирование дефицита).</summary>
    public static double MoneyPrinting(double deficit, double gdp) =>
        Math.Max(deficit, 0.0) / Math.Max(gdp, 1.0) * MoneyPrintingInflationFactor;

    /// <summary>Инфляционный вклад перегрева спроса (по всем товарам).</summary>
    public static double DemandPull(double[] demand, double[] supply, int goodCount)
    {
        double sum = 0.0;
        for (int g = 0; g < goodCount; g++)
            sum += Math.Max(demand[g] - supply[g], 0.0) / Math.Max(supply[g], 1.0);
        return sum / Math.Max(goodCount, 1) * DemandInflationFactor;
    }

    /// <summary>Шаг инфляции со сглаживанием 0.2, кламп [-0.05, 3.0].</summary>
    public static double InflationStep(double current, double baseInflation, double moneyPrinting, double demandPull)
    {
        double target = baseInflation + moneyPrinting + demandPull;
        return Math.Clamp(current + (target - current) * 0.2, -0.05, 3.0);
    }

    /// <summary>Долг: дефицит (>0) наращивает, профицит (<0) гасит, кламп >= 0.</summary>
    public static double DebtNext(double debt, double deficit) =>
        Math.Max(debt + deficit, 0.0);

    /// <summary>Процентная ставка от базовой + долговой нагрузки + инфляции.</summary>
    public static double InterestRate(double baseRate, double debtRatio, double inflation) =>
        Math.Clamp(baseRate + debtRatio * 0.05 + Math.Max(inflation, 0.0) * 0.3, 0.001, 0.5);

    /// <summary>Кредитный рейтинг 0..9 (0=AAA .. 7=CC; порядок = CreditRating enum).</summary>
    public static int RatingFromDebt(double debtRatio)
    {
        if (debtRatio < 0.3) return 0; // AAA
        if (debtRatio < 0.5) return 1; // AA
        if (debtRatio < 0.7) return 2; // A
        if (debtRatio < 0.9) return 3; // BBB
        if (debtRatio < 1.1) return 4; // BB
        if (debtRatio < 1.3) return 5; // B
        if (debtRatio < 1.5) return 6; // CCC
        return 7; // CC
    }

    // ===================== БОЙ =====================

    /// <summary>Вероятность победы атакующего (0..1); при нулевых силах 0.5.</summary>
    public static double CombatWinChance(double attackPower, double defensePower)
    {
        double total = attackPower + defensePower;
        return total <= 0 ? 0.5 : attackPower / total;
    }

    /// <summary>Доля потерь (0..1) от casualties к totalUnits.</summary>
    public static double CasualtyShare(int totalUnits, int casualties) =>
        totalUnits <= 0 ? 0.0 : Math.Clamp((double)casualties / totalUnits, 0.0, 1.0);

    // ===================== ТОРГОВЛЯ =====================

    /// <summary>
    /// Эффективность торгового потока (кламп 0..2).
    /// agreement: 0=None 1=FreeTrade 2=Preferential 3=CustomsUnion 4=CommonMarket.
    /// </summary>
    public static double TradeEfficiency(double relations01, int agreement, bool sanctioned, double distance)
    {
        double eff = 0.3 + 0.7 * Math.Clamp(relations01, 0.0, 1.0);
        eff *= agreement switch
        {
            1 => 1.5,
            2 => 1.3,
            3 => 1.8,
            4 => 2.0,
            _ => 1.0,
        };
        if (sanctioned)
            eff *= 0.4;
        eff *= 1.0 / (1.0 + distance / 800.0);
        return Math.Clamp(eff, 0.0, 2.0);
    }

    private static int ClampInt(long v) => (int)Math.Clamp(v, 0L, int.MaxValue);
}
