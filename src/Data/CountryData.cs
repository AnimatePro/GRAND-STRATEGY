using System.Collections.Generic;
using Godot;

namespace GrandStrategy.Data;

/// <summary>
/// Данные страны (класс — изменяемая сущность верхнего уровня).
/// Макроэкономика, дипломатические отношения, законы, модификаторы, списки провинций.
/// </summary>
public sealed class CountryData
{
    public int Id;
    public string Code = string.Empty;      // ISO 3166-1 alpha-3, напр. "RUS"
    public string NameKey = string.Empty;   // английское название
    public string NameRu = string.Empty;    // русское название
    public string FlagId = string.Empty;    // идентификатор флага (assets)
    public Color Color = Colors.Gray;

    public GovernmentType GovernmentType = GovernmentType.Republic;
    public Ideology Ideology = Ideology.None;

    public float Stability = 50f;      // 0..100
    public float Legitimacy = 50f;     // 0..100
    public float WarExhaustion = 0f;   // 0..100

    public double Treasury = 0.0;      // казна
    public int CurrencyId = -1;        // индекс валюты
    public double Debt = 0.0;          // гос. долг
    public CreditRating CreditRating = CreditRating.A;
    public double Inflation = 0.02;    // годовая доля, 0.02 = 2%
    public double BaseInterestRate = 0.04;

    public double Gdp = 0.0;           // номинальный ВВП
    public long Population = 0;        // кэш суммы населения провинций
    public float Urbanization = 0f;    // 0..1
    public float Literacy = 0.5f;      // 0..1
    public float TechLevel = 1f;       // множитель технологий

    public int[] Laws = System.Array.Empty<int>();   // id принятых законов

    /// <summary>Отношения с другими странами: countryId -> [-100..100].</summary>
    public Dictionary<int, float> Relations = new();

    public TradePolicy TradePolicy = new();
    public AiProfile AiProfile = AiProfile.Balanced;

    public bool IsPlayer;
    public bool IsAlive = true;

    public int CapitalProvinceId = -1;
    public List<int> OwnedProvinceIds = new();
    public List<int> ControlledProvinceIds = new();

    public ModifierList Modifiers = new();

    public float RelationWith(int countryId) =>
        Relations.TryGetValue(countryId, out float v) ? v : 0f;

    public void SetRelation(int countryId, float value) =>
        Relations[countryId] = Mathf.Clamp(value, -100f, 100f);
}

/// <summary>Торговая политика страны.</summary>
public sealed class TradePolicy
{
    public ExchangeRateRegime ExchangeRegime = ExchangeRateRegime.Floating;
    public float ImportTariff = 0.05f;   // средний импортный тариф, доля
    public float ExportTariff = 0.0f;
    public bool FreeTrade = false;       // глобальный режим свободной торговли
    public HashSet<int> Embargoed = new();   // страны под эмбарго
    public HashSet<int> Sanctioned = new();  // страны под санкциями
    public Dictionary<int, TradeAgreement> Agreements = new(); // страна -> тип соглашения
}
