using System;
using System.Collections.Generic;
using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;
using GrandStrategy.Systems.Population;
using GrandStrategy.Systems.Governance;
using GrandStrategy.Systems.Tech;
using GrandStrategy.Systems.Trade;
using GrandStrategy.SimCore;

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
            eco.BaselineGdp = world.Countries[c].Gdp; // реальный стартовый ВВП
            eco.Gdp = world.Countries[c].Gdp;
            eco.GdpPerCapita = world.Countries[c].Gdp / Math.Max(world.Countries[c].Population, 1L);
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
        StepForeignEconomy(world);

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

            // Мёртвые (аннексированные) страны: экономика замораживается.
            if (!world.Countries[c].IsAlive)
            {
                eco.Employment = 0;
                eco.UnemploymentRate = 0;
                continue;
            }

            // Стабильность с бонусом от законов.
            double stability = world.Countries[c].Stability + world.AggregateLaws(world.Countries[c]).StabilityBonus;
            stability = Math.Clamp(stability, 0.0, 100.0);
            eco.Employment = eco.LaborForce * (0.9 + 0.1 * stability / 100.0);
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

            // Эффекты зданий провинции.
            double devBonus = 0.0, infraBonus = 0.0;
            foreach (int bid in p.BuildingIds)
            {
                if (bid >= 0 && bid < world.Buildings.Length)
                {
                    devBonus += world.Buildings[bid].DevelopmentBonus;
                    infraBonus += world.Buildings[bid].InfrastructureBonus;
                }
            }
            double dev = Math.Clamp(p.Development + devBonus, 0.01, 2.0);
            double infra = 0.5 + 0.5 * Math.Clamp(p.Infrastructure + infraBonus, 0.0, 1.0);

            // Продовольствие (сельские работники провинции).
            double rural = PopulationSystem.LaborForce(p) * (1.0 - world.Countries[p.OwnerId].Urbanization);
            int foodId = FoodGoodId(world);
            if (foodId >= 0)
                production[foodId] += rural * EconomyConstants.RuralFoodPerWorker * dev * infra
                    * TechManager.Instance.FoodMult(p.OwnerId);

            // Добыча ресурсов. Реальные рудники (ResourceAmounts в тоннах) дают больше.
            foreach (int rid in p.ResourceIds)
            {
                if (rid < 0 || rid >= production.Length)
                    continue;
                double baseAmount = EconomyConstants.BaseExtraction * dev * infra;
                if (p.ResourceAmounts.Length > rid && p.ResourceAmounts[rid] > 0)
                {
                    // Реальный рудник: объём добычи (тонны) масштабируется.
                    double real = p.ResourceAmounts[rid] / 100.0; // 100 тонн -> 1 ед.
                    baseAmount = Math.Max(baseAmount, real);
                }
                production[rid] += baseAmount;
            }
        }

        // Реальная добыча по странам (production.csv): нефть/газ/уголь/железо масштабируются
        // по фактическим объёмам производства, распределяясь по ресурсным провинциям.
        ApplyNationalProduction(world);

        // Промышленность (цепочки сырьё -> товары).
        StepIndustry(world);
    }

    /// <summary>Реальная добыча страны (production.csv) добавляется поверх провинциальной.</summary>
    private void ApplyNationalProduction(WorldData world)
    {
        // Индексы товаров (0-based, порядок data/goods.csv).
        const int IronId = 3, CoalId = 4, OilId = 5, GasId = 6;

        for (int c = 0; c < world.CountryCount; c++)
        {
            CountryData country = world.Countries[c];
            if (country == null || !world.ProductionByCode.TryGetValue(country.Code, out ProductionData prod))
                continue;

            CountryEconomy eco = Economy.Countries[c];
            double dev = 0.5 + 0.5 * Math.Clamp(country.Urbanization, 0.0, 1.0);

            // Нефть (тыс. барр/день -> годовой масштаб), газ (млрд м3), уголь/железо (млн т).
            if (prod.OilKbd > 0)
                eco.Production[OilId] += prod.OilKbd * 0.4 * dev;
            if (prod.GasBcm > 0)
                eco.Production[GasId] += prod.GasBcm * 0.6 * dev;
            if (prod.CoalMt > 0)
                eco.Production[CoalId] += prod.CoalMt * 0.8 * dev;
            if (prod.IronMt > 0)
                eco.Production[IronId] += prod.IronMt * 0.9 * dev;
        }
    }

    // Промышленное производство: товары производятся из сырья (производственные цепочки).
    private void StepIndustry(WorldData world)
    {
        for (int c = 0; c < world.CountryCount; c++)
        {
            CountryEconomy eco = Economy.Countries[c];
            double industrial = eco.Employment * world.Countries[c].Urbanization;
            double capacity = industrial * EconomyConstants.IndustryProductivity
                * TechManager.Instance.ProductionMult(c)
                * DifficultyModifiers.EconomyMult(c);

            // Распределяем промышленный потенциал по производящимся товарам,
            // ограничивая каждое производство доступным сырьём (рецепт).
            double totalDesired = 0.0;
            for (int g = 0; g < world.GoodCount; g++)
                if (world.Goods[g].Inputs.Length > 0)
                    totalDesired += 1.0; // каждое производство претендует на равную долю

            if (totalDesired > 0)
            {
                foreach (int g in ProducingGoods(world))
                {
                    GoodData good = world.Goods[g];
                    double share = capacity / totalDesired;

                    // Лимит по сырью: максимум продукции = min(доступное_сырьё / расход).
                    double inputLimit = double.MaxValue;
                    for (int i = 0; i < good.Inputs.Length; i++)
                    {
                        int inId = good.Inputs[i];
                        double avail = eco.Production[inId]; // добытое сырьё этого хода
                        double need = good.InputAmounts[i];
                        if (need > 0)
                            inputLimit = Math.Min(inputLimit, avail / need);
                    }

                    double produced = Math.Min(share, inputLimit);
                    if (produced <= 0)
                        continue;

                    // Расход сырья (потребление в производстве).
                    for (int i = 0; i < good.Inputs.Length; i++)
                        eco.Production[good.Inputs[i]] -= produced * good.InputAmounts[i];

                    eco.Production[g] += produced;
                }
            }
        }
    }

    /// <summary>Товары с производственным рецептом (входное сырьё).</summary>
    private static IEnumerable<int> ProducingGoods(WorldData world)
    {
        for (int g = 0; g < world.GoodCount; g++)
            if (world.Goods[g].Inputs.Length > 0)
                yield return g;
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
                eco.Price[g] = SimFormulas.PriceFor(good.BasePrice, eco.Supply[g], eco.Demand[g], good.ElasticityDemand);
            }
        }
    }

    // --- 7. Доходы предприятий и 8. Зарплаты ---------------------------------

    private void StepWages(WorldData world)
    {
        for (int c = 0; c < world.CountryCount; c++)
        {
            CountryEconomy eco = Economy.Countries[c];

            // Средняя зарплата ≈ доля труда в ВВП на занятого (реалистичный масштаб).
            double gdp = eco.Gdp > 0 ? eco.Gdp : eco.BaselineGdp;
            double wageBill = gdp * EconomyConstants.WageShare;
            eco.AvgWage = wageBill / Math.Max(eco.Employment, 1.0);
        }
    }

    // --- 9. Налоги -----------------------------------------------------------

    private void StepTaxes(WorldData world)
    {
        for (int c = 0; c < world.CountryCount; c++)
        {
            CountryEconomy eco = Economy.Countries[c];

            // Налоговые поступления ≈ доля ВВП (реалистично 15-40% в зависимости от ставок).
            // Это привязывает бюджет к реальному масштабу ВВП, а не к игровым единицам.
            double gdp = eco.Gdp > 0 ? eco.Gdp : eco.BaselineGdp;
            double effectiveRate =
                eco.Taxes.Income * 0.45 +      // подоходный
                eco.Taxes.Corporate * 0.15 +   // на прибыль
                eco.Taxes.Vat * 0.35 +         // НДС
                eco.Taxes.Resource * 0.05;     // ресурсный
            effectiveRate = Math.Clamp(effectiveRate, 0.02, 0.60);

            double lawTaxMult = world.AggregateLaws(world.Countries[c]).TaxMult;
            double advisorMult = AdvisorManager.Instance.AdvisorMult(c, AdvisorDomain.Economy);

            eco.BudgetRevenue = gdp * effectiveRate
                * TechManager.Instance.TaxMult(c) * lawTaxMult * advisorMult;
        }
    }

    // --- 10. Бюджет ----------------------------------------------------------

    private void StepBudget(WorldData world)
    {
        // Предрасчёт содержания зданий по странам (однократный проход по провинциям).
        double[] buildingUpkeep = new double[world.CountryCount];
        for (int i = 0; i < world.ProvinceCount; i++)
        {
            ProvinceData p = world.Provinces[i];
            if (p.OwnerId < 0)
                continue;
            foreach (int bid in p.BuildingIds)
                if (bid >= 0 && bid < world.Buildings.Length)
                    buildingUpkeep[p.OwnerId] += world.Buildings[bid].Upkeep;
        }

        for (int c = 0; c < world.CountryCount; c++)
        {
            CountryEconomy eco = Economy.Countries[c];
            double revenue = eco.BudgetRevenue;
            double debtService = eco.Debt * eco.InterestRate;
            double discretionary = Math.Max(revenue - debtService, 0.0);

            double lawUpkeep = world.AggregateLaws(world.Countries[c]).UpkeepPerTurn;

            double expenses = debtService + buildingUpkeep[c] + lawUpkeep;
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
            // Знаковый баланс: >0 = дефицит, <0 = профицит (профицит гасит долг/пополняет казну).
            eco.Deficit = expenses - revenue;
        }
    }

    // --- 11. Инфляция --------------------------------------------------------

    private void StepInflation(WorldData world)
    {
        for (int c = 0; c < world.CountryCount; c++)
        {
            CountryEconomy eco = Economy.Countries[c];
            double baseInflation = world.Countries[c].Inflation;
            double moneyPrinting = SimFormulas.MoneyPrinting(eco.Deficit, eco.Gdp);
            double demandPull = SimFormulas.DemandPull(eco.Demand, eco.Supply, world.GoodCount);

            eco.Inflation = SimFormulas.InflationStep(eco.Inflation, baseInflation, moneyPrinting, demandPull);
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

            // Дефицит наращивает долг, профицит гасит (Deficit — знаковый).
            eco.Debt = SimFormulas.DebtNext(eco.Debt, eco.Deficit);

            double debtRatio = eco.Debt / Math.Max(eco.Gdp, 1.0);
            eco.DebtToGdp = debtRatio;
            eco.InterestRate = SimFormulas.InterestRate(country.BaseInterestRate, debtRatio, eco.Inflation);

            country.CreditRating = (CreditRating)SimFormulas.RatingFromDebt(debtRatio);

            // Дефолт: долг > порога ВВП -> списание части, потеря резервов, инфляционный шок.
            if (debtRatio > SimFormulas.DefaultDebtToGdp)
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
            double productionValue = 0.0;
            for (int g = 0; g < world.GoodCount; g++)
                productionValue += eco.Production[g] * eco.Price[g];

            // Инициализируем базовое производство на первом тике.
            if (eco.BaselineProductionValue <= 0)
                eco.BaselineProductionValue = Math.Max(productionValue, 1.0);

            // ВВП = реальный базовый ВВП × индекс экономической активности
            // (текущее производство относительно стартового). Так ВВП остаётся
            // реалистичным по масштабу и реагирует на войну/кризис/рост.
            double baseline = eco.BaselineGdp > 0 ? eco.BaselineGdp : 1.0;
            double activity = productionValue / Math.Max(eco.BaselineProductionValue, 1.0);
            activity = Math.Clamp(activity, 0.2, 5.0); // не падает ниже 20%, не растёт выше 5x

            double targetGdp = baseline * activity;
            if (eco.Gdp <= 0)
                eco.Gdp = targetGdp;
            eco.Gdp += (targetGdp - eco.Gdp) * 0.2; // плавная подстройка

            eco.Gdp = Math.Max(eco.Gdp, 1.0);
            eco.GdpPerCapita = eco.Gdp / Math.Max(world.Countries[c].Population, 1L);
            world.Countries[c].Gdp = eco.Gdp;
            world.Countries[c].Treasury = Math.Max(world.Countries[c].Treasury - eco.Deficit, 0.0);
        }
    }

    // --- 14. Внешняя экономика (платёжный баланс, резервы, курс) --------------

    private void StepForeignEconomy(WorldData world)
    {
        for (int c = 0; c < world.CountryCount; c++)
        {
            CountryEconomy eco = Economy.Countries[c];
            CountryData country = world.Countries[c];
            if (eco == null || country == null)
                continue;

            // Счёт текущих операций = торговый баланс + услуги (2% ВВП) + переводы.
            double services = eco.Gdp * 0.02;
            eco.CurrentAccount = eco.TradeBalance + services + eco.Remittances;

            // Счёт капитала: приток инвестиций пропорционален разнице ставок,
            // отток — при нестабильности/высокой инфляции.
            double interestDifferential = (EconomyAverageInterest() - eco.InterestRate) * 0.05;
            double stabilityFactor = (country.Stability - 50.0) / 1000.0;
            eco.CapitalAccount = eco.Gdp * (interestDifferential + stabilityFactor);

            // Дефицит текущего счёта покрывается резервами; профицит пополняет их.
            double netFlow = eco.CurrentAccount + eco.CapitalAccount;
            if (netFlow < 0)
            {
                // Покрываем из резервов; если их не хватает — берём внешний долг.
                double need = -netFlow;
                double fromReserves = Math.Min(eco.Reserves, need);
                eco.Reserves -= fromReserves;
                need -= fromReserves;
                if (need > 0)
                    eco.ExternalDebt += need;
            }
            else
            {
                // Профицит гасит внешний долг, остаток — в резервы.
                double surplus = netFlow;
                double payDebt = Math.Min(eco.ExternalDebt, surplus);
                eco.ExternalDebt -= payDebt;
                eco.Reserves += (surplus - payDebt);
            }

            // Обменный курс (плавающий): зависит от торгового баланса,
            // дифференциала инфляции и процентных ставок.
            double inflationDiff = eco.Inflation - EconomyAverageInflation();
            double tradePressure = eco.TradeBalance / Math.Max(eco.Gdp, 1.0);
            double targetRate = 1.0 + tradePressure * 2.0 + inflationDiff * 0.5 - interestDifferential;
            targetRate = Math.Clamp(targetRate, 0.2, 5.0);
            eco.ExchangeRate += (targetRate - eco.ExchangeRate) * 0.1; // плавная подстройка

            // Обслуживание внешнего долга: процент со списанием из резервов/казны.
            double extDebtService = eco.ExternalDebt * eco.InterestRate;
            if (extDebtService > 0)
            {
                double pay = Math.Min(eco.Reserves, extDebtService);
                eco.Reserves -= pay;
                double remaining = extDebtService - pay;
                if (remaining > 0)
                {
                    // Не хватает резервов — списываем с казны и добавляем в расходы.
                    country.Treasury = Math.Max(country.Treasury - remaining, 0.0);
                }
            }

            // Резервы не могут быть отрицательными.
            eco.Reserves = Math.Max(eco.Reserves, 0.0);

            // Синхронизация с CountryData.
            country.BaseInterestRate = eco.InterestRate;
        }
    }

    private double EconomyAverageInterest()
    {
        double sum = 0.0;
        for (int c = 0; c < Economy.Countries.Length; c++)
            sum += Economy.Countries[c].InterestRate;
        return Economy.Countries.Length > 0 ? sum / Economy.Countries.Length : 0.04;
    }

    private double EconomyAverageInflation()
    {
        double sum = 0.0;
        for (int c = 0; c < Economy.Countries.Length; c++)
            sum += Economy.Countries[c].Inflation;
        return Economy.Countries.Length > 0 ? sum / Economy.Countries.Length : 0.02;
    }

    // --- helpers -------------------------------------------------------------

    private static int FoodGoodId(WorldData world)
    {
        for (int g = 0; g < world.GoodCount; g++)
            if (world.Goods[g].Category == GoodCategory.Food)
                return g;
        return -1;
    }
}
