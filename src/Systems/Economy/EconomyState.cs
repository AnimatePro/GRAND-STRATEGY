using System;
using GrandStrategy.Data;

namespace GrandStrategy.Systems.Economy;

/// <summary>Ставки налогов страны (доли 0..1).</summary>
public sealed class TaxRates
{
    public double Income = 0.12;     // подоходный
    public double Corporate = 0.15;  // на прибыль
    public double Vat = 0.10;        // НДС
    public double Property = 0.02;   // имущественный
    public double Resource = 0.10;   // на добычу ресурсов
    public double ImportTariff = 0.05;
    public double ExportTariff = 0.0;
    public double Luxury = 0.15;
}

/// <summary>План расходов государства (доли от бюджета).</summary>
public sealed class SpendingPlan
{
    public double Administration = 0.10;
    public double Military = 0.15;
    public double Education = 0.12;
    public double Healthcare = 0.12;
    public double Infrastructure = 0.10;
    public double Welfare = 0.15;
    public double Research = 0.05;
    public double DebtService = 0.08; // пересчитывается по факту
    public double Subsidies = 0.05;
    public double Diplomacy = 0.02;
    public double Security = 0.06;
}

/// <summary>Рыночное состояние товара в стране.</summary>
public struct GoodMarket
{
    public double Supply;
    public double Demand;
    public double Price;
}

/// <summary>
/// Макроэкономика страны (пересчитывается каждый тик). Хранится отдельно от
/// CountryData, чтобы не раздувать горячую структуру и не смешивать слои.
/// </summary>
public sealed class CountryEconomy
{
    public double LaborForce;
    public double Employment;
    public double UnemploymentRate; // 0..1

    public double Gdp;              // номинальный ВВП
    public double GdpPerCapita;
    public double AvgWage;
    public double BaselineGdp;      // реальный стартовый ВВП (якорь из данных)

    public double BudgetRevenue;
    public double BudgetExpenses;
    public double Deficit;          // >0 = дефицит
    public double TradeBalance;     // экспорт - импорт

    public double Inflation;        // годовая доля
    public double InterestRate;
    public double Debt;
    public double DebtToGdp;
    public double Reserves;

    // --- Внешняя экономика (платёжный баланс, обменный курс) ---
    public double ExchangeRate = 1.0;   // за 1 единицу базовой валюты (условно USD)
    public double CurrentAccount;       // счёт текущих операций (торговля + услуги + трансферты)
    public double CapitalAccount;       // счёт капитала (инвестиции + займы)
    public double ExternalDebt;         // внешний долг (займы у других стран)
    public double Remittances;          // денежные переводы (диаспора)

    public TaxRates Taxes = new();
    public SpendingPlan Spending = new();

    // Товарные рынки (индекс = id товара).
    public double[] Supply = Array.Empty<double>();
    public double[] Demand = Array.Empty<double>();
    public double[] Price = Array.Empty<double>();
    public double[] Production = Array.Empty<double>();
    public double[] Consumption = Array.Empty<double>();

    public void Allocate(int goodCount)
    {
        Supply = new double[goodCount];
        Demand = new double[goodCount];
        Price = new double[goodCount];
        Production = new double[goodCount];
        Consumption = new double[goodCount];
    }
}

/// <summary>Контейнер экономик всех стран.</summary>
public sealed class WorldEconomy
{
    public CountryEconomy[] Countries = Array.Empty<CountryEconomy>();

    public void Allocate(WorldData world)
    {
        Countries = new CountryEconomy[world.CountryCount];
        for (int i = 0; i < world.CountryCount; i++)
        {
            Countries[i] = new CountryEconomy();
            Countries[i].Allocate(world.GoodCount);
        }
    }
}
