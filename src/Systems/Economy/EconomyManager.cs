using System;
using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;
using GrandStrategy.Systems.Population;
using GrandStrategy.Systems.Tech;
using GrandStrategy.Systems.Trade;

namespace GrandStrategy.Systems.Economy;

/// <summary>
/// Менеджер экономики (Autoload #9). Выполняет фиксированный порядок тиков экономики
/// (см. список ниже) каждый ход. Демография — раз в год, миграция — раз в месяц.
/// Все величины клампятся, деления защищены от нуля — NaN/Infinity исключены.
/// </summary>
public partial class EconomyManager : Node
{
    public static EconomyManager Instance { get; private set; } = null!;

    public WorldEconomy Economy { get; private set; } = new();

    private int _lastDemographyYear = -1;
    private int _lastMigrationMonth = -1;

    public override void _Ready()
    {
        Instance = this;
        EventBus.Instance.GameStarted += OnGameStarted;
    }

    private void OnGameStarted() => Reset();

    public void Reset()
    {
        WorldData world = DataManager.Instance.World;
        Economy.Allocate(world);

        for (int c = 0; c < world.CountryCount; c++)
        {
            CountryEconomy eco = Economy.Countries[c];
            for (int g = 0; g < world.GoodCount; g++)
            {
                eco.Price[g] = world.Goods[g].BasePrice;
                eco.Supply[g] = 0;
                eco.Demand[g] = 0;
            }
            eco.Debt = world.Countries[c].Debt;
            eco.InterestRate = world.Countries[c].BaseInterestRate;
            eco.Inflation = world.Countries[c].Inflation;
            eco.Reserves = world.Countries[c].Treasury;
        }
        _lastDemographyYear = -1;
        _lastMigrationMonth = -1;
    }

    /// <summary>Один экономический тик (вызывается из GameManager.EndTurn).</summary>
    public void Tick()
    {
        WorldData world = DataManager.Instance.World;
        if (!DataManager.Instance.IsLoaded || world.CountryCount == 0)
            return;
        if (Economy.Countries.Length != world.CountryCount)
            Reset(); // защита от тика до старта игры

        int year = TimeManager.Instance.CurrentYear;
        if (_lastDemographyYear != year)
        {
            PopulationSystem.YearTick(world);
            _lastDemographyYear = year;
        }

        int month = year * 12 + TimeManager.Instance.DayOfYear / 30;
        if (_lastMigrationMonth != month)
        {
            PopulationSystem.MigrateTick(world, GdpPerCapitaOf);
            _lastMigrationMonth = month;
        }

        // Фиксированный порядок тиков.
        StepLabor(world);
        StepProduction(world);
        StepSupplyDemand(world);
        TradeManager.Instance.Tick(world, Economy);
        StepPrices(world);
        StepWages(world);
        StepTaxes(world);
        StepBudget(world);
        StepInflation(world);
        StepDebt(world);
        StepGdp(world);

        EventBus.Instance.EmitEconomyUpdated();
    }

    private double GdpPerCapitaOf(ProvinceData p)
    {
        if (p.OwnerId >= 0 && p.OwnerId < Economy.Countries.Length)
        {
            double pc = Economy.Countries[p.OwnerId].GdpPerCapita;
            if (pc > 0)
                return pc;
        }
        return 1000.0;
    }

    // --- 1-2. Труд -----------------------------------------------------------

    private void StepLabor(WorldData world)
    {
        double[] laborByCountry = new double[world.CountryCount];
        long[] popByCountry = new long[world.CountryCount];

        for (int i = 0; i < world.ProvinceCount; i++)
        {
            ProvinceData p = world.Provinces[i];
            if (p.OwnerId < 0)
                continue;
            laborByCountry[p.OwnerId] += PopulationSystem.LaborForce(p);
            popByCountry[p.OwnerId] += p.TotalPopulation;
        }

        for (int c = 0; c < world.CountryCount; c++)
        {
            CountryEconomy eco = Economy.Countries[c];
            eco.LaborForce = Math.Max(laborByCountry[c], 1.0);
            world.Countries[c].Population = popByCountry[c];
            eco.Employment = eco.LaborForce * (0.9 + 0.1 * world.Countries[c].Stability / 100.0);
            eco.UnemploymentRate = Math.Clamp(1.0 - eco.Employment / Math.Max(eco.LaborForce, 1.0), 0.0, 1.0);
        }
    }

    // --- 3. Производство -----------------------------------------------------

    private void StepProduction(WorldData world)
    {
        // Обнуление.
        for (int c = 0; c < world.CountryCount; c++)
            Array.Clear(Economy.Countries[c].Production, 0, Economy.Countries[c].Production.Length);

        // Провинциальное производство: продовольствие + добыча ресурсов.
        for (int i = 0; i < world.ProvinceCount; i++)
        {
            ProvinceData p = world.Provinces[i];
            if (p.OwnerId < 0)
                continue;

            double[] production = Economy.Countries[p.OwnerId].Production;
            double dev = Math.Clamp(p.Development, 0.01, 1.0);
            double infra = 0.5 + 0.5 * Math.Clamp(p.Infrastructure, 0.0, 1.0);

            // Продовольствие (сельские работники провинции).
            double rural = PopulationSystem.LaborForce(p) * (1.0 - world.Countries[p.OwnerId].Urbanization);
            int foodId = FoodGoodId(world);
            if (foodId >= 0)
                production[foodId] += rural * EconomyConstants.RuralFoodPerWorker * dev * infra
                    * TechManager.Instance.FoodMult(p.OwnerId);

            // Добыча ресурсов.
            foreach (int rid in p.ResourceIds)
            {
                if (rid >= 0 && rid < production.Length)
                    production[rid] += EconomyConstants.BaseExtraction * dev * infra;
            }
        }

        // Промышленное производство (индустриальные работники распределяются по товарам).
        for (int c = 0; c < world.CountryCount; c++)
        {
            CountryEconomy eco = Economy.Countries[c];
            double industrial = eco.Employment * world.Countries[c].Urbanization;
            double output = industrial * EconomyConstants.IndustryProductivity
                * TechManager.Instance.ProductionMult(c);

            int manuCount = 0;
            for (int g = 0; g < world.GoodCount; g++)
                if (world.Goods[g].Category is GoodCategory.Manufactured or GoodCategory.Strategic)
                    manuCount++;

            if (manuCount > 0)
            {
                double perGood = output / manuCount;
                for (int g = 0; g < world.GoodCount; g++)
                    if (world.Goods[g].Category is GoodCategory.Manufactured or GoodCategory.Strategic)
                        eco.Production[g] += perGood;
            }
        }
    }

    // --- 4. Спрос/предложение ------------------------------------------------

    private void StepSupplyDemand(WorldData world)
    {
        for (int c = 0; c < world.CountryCount; c++)
        {
            CountryEconomy eco = Economy.Countries[c];
            CountryData country = world.Countries[c];
            double pop = Math.Max(country.Population, 1L);
            double wealth = Math.Clamp((eco.GdpPerCapita > 0 ? eco.GdpPerCapita : 5000.0) / 5000.0, 0.2, 5.0);
            double industrial = eco.Employment * country.Urbanization;

            for (int g = 0; g < world.GoodCount; g++)
            {
                GoodData good = world.Goods[g];
                eco.Supply[g] = eco.Production[g];

                double demand = good.Category switch
                {
                    GoodCategory.Food => pop * EconomyConstants.FoodPerCapita,
                    GoodCategory.RawMaterial => industrial * 0.8,
                    GoodCategory.Energy => industrial * 1.2 + pop * 0.2,
                    GoodCategory.Manufactured => pop * 0.4 * wealth,
                    GoodCategory.Luxury => pop * 0.08 * wealth,
                    GoodCategory.Strategic => industrial * 0.5,
                    _ => pop * 0.2,
                };
                eco.Demand[g] = demand;
            }
        }
    }

    // --- 5. Цены -------------------------------------------------------------

    private void StepPrices(WorldData world)
    {
        for (int c = 0; c < world.CountryCount; c++)
        {
            CountryEconomy eco = Economy.Countries[c];
            for (int g = 0; g < world.GoodCount; g++)
            {
                GoodData good = world.Goods[g];
                double supply = Math.Max(eco.Supply[g], 1.0);
                double demand = eco.Demand[g];
                double ratio = demand / supply;
                double factor = Math.Pow(ratio, good.ElasticityDemand);
                double price = good.BasePrice * factor;
                eco.Price[g] = Math.Clamp(price,
                    good.BasePrice * EconomyConstants.MinPriceMult,
                    good.BasePrice * EconomyConstants.MaxPriceMult);
            }
        }
    }

    // --- 7. Доходы предприятий и 8. Зарплаты ---------------------------------

    private void StepWages(WorldData world)
    {
        for (int c = 0; c < world.CountryCount; c++)
        {
            CountryEconomy eco = Economy.Countries[c];
            double totalOutputValue = 0.0;
            for (int g = 0; g < world.GoodCount; g++)
                totalOutputValue += eco.Production[g] * eco.Price[g];

            double wageBill = totalOutputValue * EconomyConstants.WageShare;
            eco.AvgWage = wageBill / Math.Max(eco.Employment, 1.0);
        }
    }

    // --- 9. Налоги -----------------------------------------------------------

    private void StepTaxes(WorldData world)
    {
        for (int c = 0; c < world.CountryCount; c++)
        {
            CountryEconomy eco = Economy.Countries[c];
            double outputValue = 0.0;
            for (int g = 0; g < world.GoodCount; g++)
                outputValue += eco.Production[g] * eco.Price[g];

            double wageBill = outputValue * EconomyConstants.WageShare;
            double profit = outputValue - wageBill;

            // База НДС — национальное потребление (стоимость спроса).
            double consumptionValue = 0.0;
            for (int g = 0; g < world.GoodCount; g++)
                consumptionValue += eco.Demand[g] * eco.Price[g];

            double income = wageBill * eco.Taxes.Income;
            double corporate = profit * eco.Taxes.Corporate;
            double vat = consumptionValue * eco.Taxes.Vat;
            double resource = outputValue * eco.Taxes.Resource * 0.3;

            eco.BudgetRevenue = (income + corporate + vat + resource) * TechManager.Instance.TaxMult(c);
        }
    }

    // --- 10. Бюджет ----------------------------------------------------------

    private void StepBudget(WorldData world)
    {
        for (int c = 0; c < world.CountryCount; c++)
        {
            CountryEconomy eco = Economy.Countries[c];
            double revenue = eco.BudgetRevenue;
            double debtService = eco.Debt * eco.InterestRate;
            double discretionary = Math.Max(revenue - debtService, 0.0);

            double expenses = debtService;
            expenses += discretionary * eco.Spending.Administration;
            expenses += discretionary * eco.Spending.Military;
            expenses += discretionary * eco.Spending.Education;
            expenses += discretionary * eco.Spending.Healthcare;
            expenses += discretionary * eco.Spending.Infrastructure;
            expenses += discretionary * eco.Spending.Welfare;
            expenses += discretionary * eco.Spending.Research;
            expenses += discretionary * eco.Spending.Subsidies;
            expenses += discretionary * eco.Spending.Diplomacy;
            expenses += discretionary * eco.Spending.Security;

            eco.BudgetExpenses = expenses;
            eco.Deficit = Math.Max(expenses - revenue, 0.0);
        }
    }

    // --- 11. Инфляция --------------------------------------------------------

    private void StepInflation(WorldData world)
    {
        for (int c = 0; c < world.CountryCount; c++)
        {
            CountryEconomy eco = Economy.Countries[c];
            // Денежная эмиссия (финансирование дефицита) + перегрев спроса.
            double baseInflation = world.Countries[c].Inflation;
            double moneyPrinting = (eco.Deficit / Math.Max(eco.Gdp, 1.0)) * EconomyConstants.MoneyPrintingInflationFactor;
            double demandPull = 0.0;
            for (int g = 0; g < world.GoodCount; g++)
                demandPull += Math.Max(eco.Demand[g] - eco.Supply[g], 0.0) / Math.Max(eco.Supply[g], 1.0);
            demandPull = demandPull / Math.Max(world.GoodCount, 1) * EconomyConstants.DemandInflationFactor;

            double target = baseInflation + moneyPrinting + demandPull;
            eco.Inflation = Math.Clamp(eco.Inflation + (target - eco.Inflation) * 0.2, -0.05, 3.0);
            world.Countries[c].Inflation = eco.Inflation;
        }
    }

    // --- 12. Долг и процент --------------------------------------------------

    private void StepDebt(WorldData world)
    {
        for (int c = 0; c < world.CountryCount; c++)
        {
            CountryEconomy eco = Economy.Countries[c];
            CountryData country = world.Countries[c];

            if (eco.Deficit > 0)
                eco.Debt += eco.Deficit;
            else
                eco.Debt = Math.Max(eco.Debt + eco.Deficit, 0.0); // профицит гасит долг

            // Процентная ставка растёт с долгом.
            double debtRatio = eco.Debt / Math.Max(eco.Gdp, 1.0);
            eco.DebtToGdp = debtRatio;
            eco.InterestRate = Math.Clamp(
                country.BaseInterestRate + debtRatio * 0.05 + Math.Max(eco.Inflation, 0) * 0.3,
                0.001, 0.5);

            // Кредитный рейтинг от долговой нагрузки.
            country.CreditRating = RatingFromDebt(debtRatio);

            // Дефолт: долг > порога ВВП -> списание части, потеря резервов, инфляционный шок.
            if (debtRatio > EconomyConstants.DefaultDebtToGdp)
            {
                eco.Debt *= 0.5;
                eco.Reserves *= 0.5;
                eco.Inflation = Math.Min(eco.Inflation + 0.15, 3.0);
                country.Stability = Math.Max(country.Stability - 5f, 0f);
                country.CreditRating = CreditRating.D;
            }

            country.Debt = eco.Debt;
            country.BaseInterestRate = eco.InterestRate;
        }
    }

    // --- 13. ВВП и статистика ------------------------------------------------

    private void StepGdp(WorldData world)
    {
        for (int c = 0; c < world.CountryCount; c++)
        {
            CountryEconomy eco = Economy.Countries[c];
            double gdp = 0.0;
            for (int g = 0; g < world.GoodCount; g++)
                gdp += eco.Production[g] * eco.Price[g];

            eco.Gdp = Math.Max(gdp, 1.0);
            eco.GdpPerCapita = eco.Gdp / Math.Max(world.Countries[c].Population, 1L);
            world.Countries[c].Gdp = eco.Gdp;
            world.Countries[c].Treasury = Math.Max(world.Countries[c].Treasury - eco.Deficit, 0.0);
        }
    }

    // --- helpers -------------------------------------------------------------

    private static int FoodGoodId(WorldData world)
    {
        for (int g = 0; g < world.GoodCount; g++)
            if (world.Goods[g].Category == GoodCategory.Food)
                return g;
        return -1;
    }

    private static CreditRating RatingFromDebt(double debtRatio)
    {
        if (debtRatio < 0.3) return CreditRating.AAA;
        if (debtRatio < 0.5) return CreditRating.AA;
        if (debtRatio < 0.7) return CreditRating.A;
        if (debtRatio < 0.9) return CreditRating.BBB;
        if (debtRatio < 1.1) return CreditRating.BB;
        if (debtRatio < 1.3) return CreditRating.B;
        if (debtRatio < 1.5) return CreditRating.CCC;
        return CreditRating.CC;
    }
}
