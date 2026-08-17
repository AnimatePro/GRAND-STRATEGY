using Xunit;
using GrandStrategy.SimCore;

namespace GrandStrategy.Tests;

/// <summary>
/// Юнит-тесты чистого ядра (SimFormulas). Запуск: `dotnet test` из tests/GrandStrategy.Tests.
/// Не требуют Godot и рантайма движка.
/// </summary>
public class SimFormulasTests
{
    [Fact]
    public void TickDemographics_NeverNegative_LongRun()
    {
        int mc = 1000, fc = 1000, mt = 500, ft = 500;
        int ma = 5000, fa = 5000, ms = 300, fs = 300;
        for (int i = 0; i < 500; i++)
            SimFormulas.TickDemographics(ref mc, ref fc, ref mt, ref ft, ref ma, ref fa, ref ms, ref fs);
        Assert.True(mc >= 0 && fc >= 0 && mt >= 0 && ft >= 0);
        Assert.True(ma >= 0 && fa >= 0 && ms >= 0 && fs >= 0);
    }

    [Fact]
    public void TickDemographics_TotalStable()
    {
        int mc = 5000, fc = 5000, mt = 3000, ft = 3000;
        int ma = 40000, fa = 40000, ms = 6000, fs = 6000;
        long before = (long)mc + fc + mt + ft + ma + fa + ms + fs;
        for (int i = 0; i < 100; i++)
            SimFormulas.TickDemographics(ref mc, ref fc, ref mt, ref ft, ref ma, ref fa, ref ms, ref fs);
        long after = (long)mc + fc + mt + ft + ma + fa + ms + fs;
        double drift = (after - before) / (double)before;
        Assert.InRange(drift, -0.2, 0.2); // за 100 лет не более ±20%
    }

    [Fact]
    public void PriceFor_Clamps_And_Monotonic()
    {
        Assert.Equal(10.0, SimFormulas.PriceFor(10, 100, 100, 1.0), 3);
        double high = SimFormulas.PriceFor(10, 10, 1000, 1.0);
        Assert.True(high > 10 && high <= 100.0);       // кап 10x
        double low = SimFormulas.PriceFor(10, 1000, 1, 1.0);
        Assert.True(low >= 1.0 && low < 10);           // пол 0.1x
    }

    [Fact]
    public void DebtNext_NeverNegative()
    {
        Assert.Equal(0.0, SimFormulas.DebtNext(0, -100), 3);
        Assert.Equal(150.0, SimFormulas.DebtNext(100, 50), 3);
        Assert.Equal(0.0, SimFormulas.DebtNext(10, -100), 3);
    }

    [Fact]
    public void InflationStep_Clamped()
    {
        Assert.InRange(SimFormulas.InflationStep(0.02, 0.02, 0, 0), -0.05, 3.0);
        Assert.InRange(SimFormulas.InflationStep(0.5, 10, 10, 10), -0.05, 3.0);
        Assert.True(SimFormulas.InflationStep(0.5, 10, 10, 10) <= 3.0);
    }

    [Fact]
    public void InterestRate_Clamped()
    {
        Assert.InRange(SimFormulas.InterestRate(0.04, 0.5, 0.02), 0.001, 0.5);
        Assert.InRange(SimFormulas.InterestRate(0.04, 10, 3.0), 0.001, 0.5);
    }

    [Fact]
    public void RatingFromDebt_Monotonic_NonDecreasing()
    {
        int prev = -1;
        foreach (double r in new[] { 0.1, 0.4, 0.6, 0.8, 1.0, 1.2, 1.4, 2.0 })
        {
            int rating = SimFormulas.RatingFromDebt(r);
            Assert.True(rating >= prev);
            prev = rating;
        }
    }

    [Fact]
    public void CombatWinChance_SumsToOne()
    {
        Assert.Equal(0.5, SimFormulas.CombatWinChance(0, 0), 6);
        Assert.Equal(1.0, SimFormulas.CombatWinChance(30, 70) + SimFormulas.CombatWinChance(70, 30), 6);
    }

    [Fact]
    public void CasualtyShare_Clamped()
    {
        Assert.Equal(1.0, SimFormulas.CasualtyShare(10, 100), 6);
        Assert.Equal(0.3, SimFormulas.CasualtyShare(100, 30), 6);
        Assert.Equal(0.0, SimFormulas.CasualtyShare(0, 10), 6);
    }

    [Fact]
    public void TradeEfficiency_Agreement_Sanction_Clamp()
    {
        Assert.InRange(SimFormulas.TradeEfficiency(0.5, 0, false, 0), 0.0, 2.0);
        Assert.True(SimFormulas.TradeEfficiency(1.0, 4, false, 0) > SimFormulas.TradeEfficiency(1.0, 0, false, 0));
        Assert.True(SimFormulas.TradeEfficiency(1.0, 0, true, 0) < SimFormulas.TradeEfficiency(1.0, 0, false, 0));
        // Очень дальнее расстояние не уводит за нижнюю границу 0.
        Assert.True(SimFormulas.TradeEfficiency(1.0, 4, false, 1e9) >= 0.0);
    }

    [Fact]
    public void LaborForce_Positive()
    {
        Assert.True(SimFormulas.LaborForce(10, 10, 10, 10, 100, 100, 10, 10) > 0);
        Assert.Equal(0.0, SimFormulas.LaborForce(0, 0, 0, 0, 0, 0, 0, 0), 6);
    }
}
