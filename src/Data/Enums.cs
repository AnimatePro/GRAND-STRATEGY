namespace GrandStrategy.Data;

// Все перечисления предметной области. Значения стабильны (сохраняются в сейвы по int),
// поэтому новые значения добавляем только в конец, не переставляя существующие.

public enum Terrain
{
    Plains = 0,
    Hills = 1,
    Mountains = 2,
    Forest = 3,
    Jungle = 4,
    Desert = 5,
    Tundra = 6,
    Marsh = 7,
    Coast = 8,
    Ocean = 9,
}

public enum Climate
{
    Tropical = 0,
    Arid = 1,
    Temperate = 2,
    Continental = 3,
    Polar = 4,
}

public enum GoodCategory
{
    Food = 0,          // зерно, скот, рыба
    RawMaterial = 1,   // руда, лес, хлопок
    Manufactured = 2,  // товары, техника
    Energy = 3,        // нефть, уголь, газ, электричество
    Luxury = 4,        // предметы роскоши
    Strategic = 5,     // сталь, алюминий, редкие металлы
}

public enum GovernmentType
{
    Monarchy = 0,
    Republic = 1,
    Theocracy = 2,
    Dictatorship = 3,
    Tribal = 4,
    ConstitutionalMonarchy = 5,
    ParliamentaryRepublic = 6,
    PresidentialRepublic = 7,
}

public enum Ideology
{
    None = 0,
    Liberalism = 1,
    Conservatism = 2,
    Socialism = 3,
    Communism = 4,
    Fascism = 5,
    Nationalism = 6,
    Theocracy = 7,
}

public enum DiplomacyStatus
{
    Neutral = 0,
    Rivalry = 1,
    Alliance = 2,
    NonAggression = 3,
    Guarantee = 4,
    Truce = 5,
    War = 6,
    Vassal = 7,
    Puppet = 8,
    Protectorate = 9,
}

public enum TradeAgreement
{
    None = 0,
    FreeTrade = 1,
    Preferential = 2,
    CustomsUnion = 3,
    CommonMarket = 4,
}

public enum ExchangeRateRegime
{
    Floating = 0,
    Fixed = 1,
    Managed = 2,
    CurrencyBoard = 3,
    CurrencyUnion = 4,
}

public enum CreditRating
{
    AAA = 0,
    AA = 1,
    A = 2,
    BBB = 3,
    BB = 4,
    B = 5,
    CCC = 6,
    CC = 7,
    C = 8,
    D = 9, // дефолт
}

public enum Difficulty
{
    VeryEasy = 0,
    Easy = 1,
    Normal = 2,
    Hard = 3,
    VeryHard = 4,
    Custom = 5,
}

public enum AiProfile
{
    Balanced = 0,
    Expansionist = 1,
    Trader = 2,
    Isolationist = 3,
    Militarist = 4,
    Diplomat = 5,
    Opportunist = 6,
    Defensive = 7,
}

public enum MapMode
{
    Political = 0,
    Terrain = 1,
    Population = 2,
    Economy = 3,
    Trade = 4,
    Resources = 5,
    Infrastructure = 6,
    Diplomacy = 7,
    War = 8,
    Unrest = 9,
    Development = 10,
    Debug = 11,
}

public enum LawCategory
{
    Economy = 0,
    Military = 1,
    Society = 2,
    Government = 3,
}

public enum ModifierStacking
{
    Additive = 0,   // суммируются
    Multiplicative = 1,
    Override = 2,   // берётся максимум
}

public enum BuildingCategory
{
    Production = 0,
    Infrastructure = 1,
    Military = 2,
    Welfare = 3,
    Education = 4,
    Health = 5,
    Resource = 6,
}
