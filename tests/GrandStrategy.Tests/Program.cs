using GrandStrategy.SimCore;

namespace GrandStrategy.Tests;

/// <summary>
/// Консольный тест-раннер чистого ядра (SimFormulas). Без xUnit/NuGet — собирается офлайн.
/// Запуск: `cd tests/GrandStrategy.Tests && dotnet run`
/// Выход: код 0 = все тесты прошли, 1 = есть ошибки.
/// </summary>
public static class Program
{
    private static int _total;
    private static int _failures;

    public static int Main()
    {
        // --- Демография ---
        TickDemographics_NeverNegative_LongRun();
        TickDemographics_TotalStable();
        LaborForce_Positive();

        // --- Экономика ---
        PriceFor_Clamps_And_Monotonic();
        DebtNext_NeverNegative();
        InflationStep_Clamped();
        InterestRate_Clamped();
        RatingFromDebt_Monotonic();

        // --- Бой ---
        CombatWinChance_SumsToOne();
        CasualtyShare_Clamped();

        // --- Торговля ---
        TradeEfficiency_Agreement_Sanction_Clamp();

        Console.WriteLine($"\n{_total} тестов, {_failures} ошибок.");
        Console.WriteLine(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILURES");
        return _failures == 0 ? 0 : 1;
    }

    private static void Check(string name, bool condition)
    {
        _total++;
        if (!condition)
        {
            _failures++;
            Console.WriteLine($"  FAIL: {name}");
        }
    }

    private static void TickDemographics_NeverNegative_LongRun()
    {
        int mc = 1000, fc = 1000, mt = 500, ft = 500, ma = 5000, fa = 5000, ms = 300, fs = 300;
        bool ok = true;
        for (int i = 0; i < 500 && ok; i++)
        {
            SimFormulas.TickDemographics(ref mc, ref fc, ref mt, ref ft, ref ma, ref fa, ref ms, ref fs);
            ok = mc >= 0 && fc >= 0 && mt >= 0 && ft >= 0 && ma >= 0 && fa >= 0 && ms >= 0 && fs >= 0;
        }
        Check("демография неотрицательна (500 лет)", ok);
    }

    private static void TickDemographics_TotalStable()
    {
        int mc = 5000, fc = 5000, mt = 3000, ft = 3000, ma = 40000, fa = 40000, ms = 6000, fs = 6000;
        long before = (long)mc + fc + mt + ft + ma + fa + ms + fs;
        for (int i = 0; i < 100; i++)
            SimFormulas.TickDemographics(ref mc, ref fc, ref mt, ref ft, ref ma, ref fa, ref ms, ref fs);
        long after = (long)mc + fc + mt + ft + ma + fa + ms + fs;
        double drift = (after - before) / (double)before;
        Check($"население стабильно (±20%/100 лет), дрейф {drift:P1}", drift > -0.2 && drift < 0.2);
    }

    private static void LaborForce_Positive()
    {
        Check("рабочая сила > 0", SimFormulas.LaborForce(10, 10, 10, 10, 100, 100, 10, 10) > 0);
        Check("рабочая сила нулевая при нулях", SimFormulas.LaborForce(0, 0, 0, 0, 0, 0, 0, 0) == 0.0);
    }

    private static void PriceFor_Clamps_And_Monotonic()
    {
        Check("цена при равновесии = базе", Math.Abs(SimFormulas.PriceFor(10, 100, 100, 1.0) - 10.0) < 1e-6);
        double high = SimFormulas.PriceFor(10, 10, 1000, 1.0);
        Check("цена не выше 10x базы", high > 10 && high <= 100.0);
        double low = SimFormulas.PriceFor(10, 1000, 1, 1.0);
        Check("цена не ниже 0.1x базы", low >= 1.0 && low < 10);
    }

    private static void DebtNext_NeverNegative()
    {
        Check("долг гасится профицитом до 0", SimFormulas.DebtNext(0, -100) == 0.0);
        Check("долг растёт от дефицита", SimFormulas.DebtNext(100, 50) == 150.0);
        Check("долг не уходит в минус", SimFormulas.DebtNext(10, -100) == 0.0);
    }

    private static void InflationStep_Clamped()
    {
        Check("инфляция в диапазоне", SimFormulas.InflationStep(0.02, 0.02, 0, 0) >= -0.05 &&
            SimFormulas.InflationStep(0.02, 0.02, 0, 0) <= 3.0);
        Check("инфляция клампится сверху", SimFormulas.InflationStep(0.5, 10, 10, 10) <= 3.0);
    }

    private static void InterestRate_Clamped()
    {
        Check("ставка в диапазоне", SimFormulas.InterestRate(0.04, 0.5, 0.02) >= 0.001 &&
            SimFormulas.InterestRate(0.04, 0.5, 0.02) <= 0.5);
        Check("ставка клампится при долговом кризисе", SimFormulas.InterestRate(0.04, 10, 3.0) <= 0.5);
    }

    private static void RatingFromDebt_Monotonic()
    {
        int prev = -1;
        bool ok = true;
        foreach (double r in new[] { 0.1, 0.4, 0.6, 0.8, 1.0, 1.2, 1.4, 2.0 })
        {
            int rating = SimFormulas.RatingFromDebt(r);
            if (rating < prev)
                ok = false;
            prev = rating;
        }
        Check("рейтинг монотонен по долгу", ok);
    }

    private static void CombatWinChance_SumsToOne()
    {
        Check("бой 0-0 = 0.5", Math.Abs(SimFormulas.CombatWinChance(0, 0) - 0.5) < 1e-6);
        Check("вероятности в сумме дают 1", Math.Abs(
            SimFormulas.CombatWinChance(30, 70) + SimFormulas.CombatWinChance(70, 30) - 1.0) < 1e-6);
    }

    private static void CasualtyShare_Clamped()
    {
        Check("потери клампятся в 1", SimFormulas.CasualtyShare(10, 100) == 1.0);
        Check("потери 30%", Math.Abs(SimFormulas.CasualtyShare(100, 30) - 0.3) < 1e-6);
        Check("потери 0 при нуле юнитов", SimFormulas.CasualtyShare(0, 10) == 0.0);
    }

    private static void TradeEfficiency_Agreement_Sanction_Clamp()
    {
        Check("эффективность в диапазоне", SimFormulas.TradeEfficiency(0.5, 0, false, 0) >= 0.0 &&
            SimFormulas.TradeEfficiency(0.5, 0, false, 0) <= 2.0);
        Check("общий рынок > без соглашения",
            SimFormulas.TradeEfficiency(1.0, 4, false, 0) > SimFormulas.TradeEfficiency(1.0, 0, false, 0));
        Check("санкции снижают поток",
            SimFormulas.TradeEfficiency(1.0, 0, true, 0) < SimFormulas.TradeEfficiency(1.0, 0, false, 0));
        Check("огромное расстояние не роняет в NaN",
            SimFormulas.TradeEfficiency(1.0, 4, false, 1e9) >= 0.0);
    }
}
