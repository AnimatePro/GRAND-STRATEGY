using System;

namespace GrandStrategy.Data;

/// <summary>
/// Товар (struct). Цена считается по формуле из M5:
/// price = base_price * спрос/предложение * транспорт * политика * шок, клампится в [0.1, 10.0].
/// </summary>
public struct GoodData
{
    public int Id;
    public string NameKey = string.Empty;
    public GoodCategory Category;
    public double BasePrice;       // базовая цена в условных единицах
    public double Price;           // текущая рыночная цена
    public double Supply;
    public double Demand;
    public bool Tradeable = true;
    public bool Strategic;
    public double StorageCost;         // стоимость хранения за единицу
    public double TransportCostMult;   // множитель транспортных издержек
    public double ElasticityDemand;    // эластичность спроса (положит. коэффициент)
    public double ElasticitySupply;    // эластичность предложения
    public int[] Inputs;               // id ресурсов для производства (рецепт)
    public double[] InputAmounts;      // количества входов
    public int[] Outputs;              // id производимых товаров (обычно 1)

    public static GoodData Create(int id, string nameKey, GoodCategory category, double basePrice)
    {
        return new GoodData
        {
            Id = id,
            NameKey = nameKey,
            Category = category,
            BasePrice = basePrice,
            Price = basePrice,
            Inputs = Array.Empty<int>(),
            InputAmounts = Array.Empty<double>(),
            Outputs = Array.Empty<int>(),
        };
    }
}

/// <summary>Валюта страны.</summary>
public struct CurrencyData
{
    public int Id;
    public string Code = string.Empty;     // ISO 4217, напр. "USD"
    public string NameKey = string.Empty;
    public double ExchangeRate = 1.0;      // за 1 единицу базовой валюты (условно USD)
    public bool IsReserveCurrency;
}
