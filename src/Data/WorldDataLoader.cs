using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using FileAccess = Godot.FileAccess; // implicit System.IO конфликтует с Godot.FileAccess

namespace GrandStrategy.Data;

/// <summary>
/// Рантайм-загрузка мира: кэшированный world.json (провинции+страны) + CSV-баланс
/// (товары, здания, законы, правительства, валюты). Собирает WorldData и строит индексы.
/// </summary>
public static class WorldDataLoader
{
    public const string WorldCachePath = "res://data/cache/world.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true, // DTO используют публичные поля
    };

    public static WorldData Load(string cachePath = WorldCachePath)
    {
        if (!FileAccess.FileExists(cachePath))
            throw new FileNotFoundException($"World cache not found: {cachePath}. Run MapImporterTool first.");

        string json = ReadText(cachePath);
        WorldFileDto? dto = JsonSerializer.Deserialize<WorldFileDto>(json, JsonOptions);
        if (dto == null)
            throw new InvalidDataException("World cache deserialized to null");

        var world = new WorldData
        {
            MapWidthPx = dto.MapWidthPx > 0 ? dto.MapWidthPx : GeoProjection.MapWidthPx,
            MapHeightPx = dto.MapHeightPx > 0 ? dto.MapHeightPx : GeoProjection.MapHeightPx,
        };

        // --- Балансовые таблицы из CSV ---
        LoadGoods(world);
        LoadCurrencies(world);
        LoadBuildings(world);
        LoadGovernments(world);
        LoadLaws(world);
        LoadReligions(world);
        LoadCultures(world);

        // --- Страны ---
        // Лидеры по двум сценариям (2024 и 1936, реальные главы государств).
        var leader2024 = new Dictionary<string, string>(StringComparer.Ordinal);
        var leader1936 = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Dictionary<string, string> row in CsvTableLoader.Load("res://data/leaders.csv"))
        {
            string code = CsvTableLoader.Str(row, "code");
            if (code.Length == 0)
                continue;
            leader2024[code] = CsvTableLoader.Str(row, "leader_2024");
            leader1936[code] = CsvTableLoader.Str(row, "leader_1936");
        }

        world.Countries = new CountryData[dto.Countries.Count];
        foreach (CountryDto cdto in dto.Countries)
        {
            var country = new CountryData
            {
                Id = cdto.Id,
                Code = cdto.Code,
                NameKey = cdto.NameKey,
                NameRu = cdto.NameRu,
                FlagId = cdto.FlagId,
                Color = ParseColor(cdto.ColorHex),
                GovernmentType = (GovernmentType)cdto.GovernmentType,
                Ideology = (Ideology)cdto.Ideology,
                CapitalProvinceId = cdto.CapitalProvinceId,
                Population = cdto.Population,
                Gdp = cdto.Gdp,
                Literacy = cdto.Literacy,
                Urbanization = cdto.Urbanization,
                Treasury = cdto.Treasury,
                StateReligionId = cdto.ReligionId,
                PrimaryCultureId = cdto.CultureId,
                Leader2024 = leader2024.TryGetValue(cdto.Code, out string? l24) ? l24 : string.Empty,
                Leader1936 = leader1936.TryGetValue(cdto.Code, out string? l36) ? l36 : string.Empty,
            };
            world.Countries[cdto.Id] = country;
            world.CountryCodeToId[cdto.Code] = cdto.Id;
        }

        // --- Провинции ---
        world.Provinces = new ProvinceData[dto.Provinces.Count];
        world.ProvinceNamesEn = new string[dto.Provinces.Count];
        world.ProvinceNamesRu = new string[dto.Provinces.Count];
        var split = new long[8];
        foreach (ProvinceDto pdto in dto.Provinces)
        {
            var province = ProvinceData.Create(pdto.Id);
            province.OwnerId = world.CountryByCode(pdto.OwnerCode);
            province.RegionId = pdto.RegionId;
            province.ContinentId = pdto.ContinentId;
            province.IsCoastal = pdto.IsCoastal;
            province.Terrain = (Terrain)pdto.Terrain;
            province.Climate = (Climate)pdto.Climate;
            province.AreaKm2 = pdto.AreaKm2;
            province.Development = pdto.Development;
            province.Infrastructure = pdto.Infrastructure;
            province.ReligionId = pdto.ReligionId;
            province.CultureId = pdto.CultureId;
            province.Centroid = new Vector2(pdto.CentroidX, pdto.CentroidY);
            province.NeighborIds = pdto.NeighborIds.ToArray();
            // Ресурсы: полный список из json (реальные рудники + ресурсы страны),
            // иначе детерминированный fallback по ландшафту.
            province.ResourceIds = pdto.ResourceIds.Count > 0
                ? pdto.ResourceIds.ToArray()
                : ResourceAssigner.Assign(pdto.Id, province.Terrain, province.Climate);
            province.CoreIds = ResolveCoreCodes(pdto.CoreCodes, world);

            // Реальные объёмы добычи рудников (goodId -> тонны).
            if (pdto.ResourceAmounts != null && pdto.ResourceAmounts.Count > 0)
            {
                var amounts = new int[world.GoodCount];
                for (int g = 0; g < world.GoodCount; g++)
                    amounts[g] = -1;
                foreach (KeyValuePair<int, int> kv in pdto.ResourceAmounts)
                    if (kv.Key >= 0 && kv.Key < world.GoodCount)
                        amounts[kv.Key] = kv.Value;
                province.ResourceAmounts = amounts;
            }
            else
            {
                province.ResourceAmounts = System.Array.Empty<int>();
            }

            SplitPopulation(pdto.TotalPopulation, split);
            province.MaleChildren = (int)split[0];
            province.FemaleChildren = (int)split[1];
            province.MaleTeens = (int)split[2];
            province.FemaleTeens = (int)split[3];
            province.MaleAdults = (int)split[4];
            province.FemaleAdults = (int)split[5];
            province.MaleSeniors = (int)split[6];
            province.FemaleSeniors = (int)split[7];

            world.Provinces[pdto.Id] = province;
            world.ProvinceIdToIndex[pdto.Id] = pdto.Id;
            world.ProvinceNamesEn[pdto.Id] = pdto.NameEn;
            world.ProvinceNamesRu[pdto.Id] = pdto.NameRu;

            if (province.OwnerId >= 0)
                world.Countries[province.OwnerId].OwnedProvinceIds.Add(pdto.Id);
        }

        // Страны без провинций (Антарктида, спорные микротерритории) — неактивны.
        foreach (CountryData c in world.Countries)
            if (c != null)
                c.IsAlive = c.OwnedProvinceIds.Count > 0;

        return world;
    }

    private static int[] ResolveCoreCodes(List<string> codes, WorldData world)
    {
        var ids = new List<int>();
        foreach (string code in codes)
        {
            int id = world.CountryByCode(code);
            if (id >= 0)
                ids.Add(id);
        }
        return ids.ToArray();
    }

    /// <summary>Разбиение населения на 8 групп по возрастно-половой структуре.</summary>
    public static void SplitPopulation(long total, long[] into)
    {
        // Доли: дети 0-10, подростки 11-17, взрослые 18-59, пожилые 60+.
        const double childShare = 0.17, teenShare = 0.09, adultShare = 0.56;
        const double femaleShare = 0.505;

        long children = (long)(total * childShare);
        long teens = (long)(total * teenShare);
        long adults = (long)(total * adultShare);
        long seniors = total - children - teens - adults;

        into[0] = (long)(children * (1 - femaleShare)); // male children
        into[1] = children - into[0];
        into[2] = (long)(teens * (1 - femaleShare));
        into[3] = teens - into[2];
        into[4] = (long)(adults * (1 - femaleShare));
        into[5] = adults - into[4];
        into[6] = (long)(seniors * (1 - femaleShare));
        into[7] = seniors - into[6];
    }

    // --- CSV-таблицы баланса -------------------------------------------------

    private static void LoadGoods(WorldData world)
    {
        var list = new List<GoodData>();
        foreach (Dictionary<string, string> row in CsvTableLoader.Load("res://data/goods.csv"))
        {
            // id в CSV — справочный; индекс в массиве — порядок строк (0-based).
            var good = GoodData.Create(
                list.Count,
                CsvTableLoader.Str(row, "name_key"),
                (GoodCategory)CsvTableLoader.Int(row, "category"),
                CsvTableLoader.Double(row, "base_price"));
            good.Strategic = CsvTableLoader.Bool(row, "strategic");
            good.Tradeable = CsvTableLoader.Bool(row, "tradeable", true);
            good.TransportCostMult = CsvTableLoader.Double(row, "transport_mult", 1.0);
            good.StorageCost = CsvTableLoader.Double(row, "storage_cost");
            good.ElasticityDemand = CsvTableLoader.Double(row, "elast_demand", 1.0);
            good.ElasticitySupply = CsvTableLoader.Double(row, "elast_supply", 1.0);
            good.Price = good.BasePrice;
            // Производственный рецепт: "inputId:amount;inputId:amount".
            string recipe = CsvTableLoader.Str(row, "inputs");
            if (!string.IsNullOrWhiteSpace(recipe))
            {
                var inIds = new List<int>();
                var inAmts = new List<double>();
                foreach (string part in recipe.Split(';'))
                {
                    string[] kv = part.Split(':');
                    if (kv.Length == 2 && int.TryParse(kv[0], out int gid) &&
                        double.TryParse(kv[1], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double amt))
                    {
                        inIds.Add(gid);
                        inAmts.Add(amt);
                    }
                }
                good.Inputs = inIds.ToArray();
                good.InputAmounts = inAmts.ToArray();
            }
            list.Add(good);
        }
        world.Goods = list.ToArray();
    }

    private static void LoadCurrencies(WorldData world)
    {
        var list = new List<CurrencyData>();
        foreach (Dictionary<string, string> row in CsvTableLoader.Load("res://data/currencies.csv"))
        {
            list.Add(new CurrencyData
            {
                Id = CsvTableLoader.Int(row, "id"),
                Code = CsvTableLoader.Str(row, "code"),
                NameKey = CsvTableLoader.Str(row, "name_key"),
                ExchangeRate = CsvTableLoader.Double(row, "exchange_rate", 1.0),
                IsReserveCurrency = CsvTableLoader.Bool(row, "reserve"),
            });
        }
        world.Currencies = list.ToArray();
    }

    private static void LoadBuildings(WorldData world)
    {
        var list = new List<BuildingData>();
        foreach (Dictionary<string, string> row in CsvTableLoader.Load("res://data/buildings.csv"))
        {
            var b = BuildingData.Create(
                CsvTableLoader.Int(row, "id"),
                CsvTableLoader.Str(row, "name_key"),
                (BuildingCategory)CsvTableLoader.Int(row, "category"));
            b.BuildCost = CsvTableLoader.Double(row, "build_cost");
            b.Upkeep = CsvTableLoader.Double(row, "upkeep");
            b.BuildTurns = CsvTableLoader.Int(row, "build_turns", 1);
            b.MaxPerProvince = CsvTableLoader.Int(row, "max_per_province", 1);
            b.RequiresCoast = CsvTableLoader.Bool(row, "requires_coast");
            b.DevelopmentBonus = CsvTableLoader.Double(row, "dev_bonus");
            b.InfrastructureBonus = CsvTableLoader.Double(row, "infra_bonus");
            list.Add(b);
        }
        world.Buildings = list.ToArray();
    }

    private static void LoadGovernments(WorldData world)
    {
        var list = new List<GovernmentData>();
        foreach (Dictionary<string, string> row in CsvTableLoader.Load("res://data/governments.csv"))
        {
            var g = GovernmentData.Create(
                CsvTableLoader.Int(row, "id"),
                (GovernmentType)CsvTableLoader.Int(row, "type"),
                CsvTableLoader.Str(row, "name_key"));
            g.TaxEfficiencyMult = CsvTableLoader.Double(row, "tax_eff", 1.0);
            g.LegitimacyBonus = CsvTableLoader.Double(row, "legitimacy_bonus");
            g.StabilityBonus = CsvTableLoader.Double(row, "stability_bonus");
            g.MilitaryCostMult = CsvTableLoader.Double(row, "military_cost", 1.0);
            g.UnrestReduction = CsvTableLoader.Double(row, "unrest_reduction");
            list.Add(g);
        }
        world.Governments = list.ToArray();
    }

    private static void LoadLaws(WorldData world)
    {
        var list = new List<LawData>();
        foreach (Dictionary<string, string> row in CsvTableLoader.Load("res://data/laws.csv"))
        {
            var l = LawData.Create(
                CsvTableLoader.Int(row, "id"),
                CsvTableLoader.Str(row, "name_key"),
                (LawCategory)CsvTableLoader.Int(row, "category"));
            l.PoliticalCost = CsvTableLoader.Double(row, "political_cost");
            l.UpkeepPerTurn = CsvTableLoader.Double(row, "upkeep");
            l.TaxMult = CsvTableLoader.Double(row, "tax_mult", 1.0);
            l.ConscriptionRate = CsvTableLoader.Double(row, "conscription_rate");
            l.StabilityBonus = CsvTableLoader.Double(row, "stability_bonus");
            l.MilitaryCostMult = CsvTableLoader.Double(row, "military_cost_mult", 1.0);
            l.ResearchMult = CsvTableLoader.Double(row, "research_mult", 1.0);
            l.TradeMult = CsvTableLoader.Double(row, "trade_mult", 1.0);
            list.Add(l);
        }
        world.Laws = list.ToArray();
    }

    /// <summary>Историческое население 1936 по странам (для сценария 1936).</summary>
    public static Dictionary<string, long> LoadPopulation1936()
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (Dictionary<string, string> row in CsvTableLoader.Load("res://data/population_1936.csv"))
        {
            string code = CsvTableLoader.Str(row, "code");
            long pop = CsvTableLoader.Long(row, "population");
            if (code.Length > 0 && pop > 0)
                result[code] = pop;
        }
        return result;
    }

    private static void LoadReligions(WorldData world)
    {
        var list = new List<ReligionData>();
        foreach (Dictionary<string, string> row in CsvTableLoader.Load("res://data/religions.csv"))
        {
            list.Add(new ReligionData
            {
                Id = list.Count,
                NameKey = CsvTableLoader.Str(row, "name_key"),
                Group = CsvTableLoader.Str(row, "group"),
            });
        }
        world.Religions = list.ToArray();
    }

    private static void LoadCultures(WorldData world)
    {
        var list = new List<CultureData>();
        foreach (Dictionary<string, string> row in CsvTableLoader.Load("res://data/cultures.csv"))
        {
            list.Add(new CultureData
            {
                Id = list.Count,
                NameKey = CsvTableLoader.Str(row, "name_key"),
                Family = CsvTableLoader.Str(row, "family"),
            });
        }
        world.Cultures = list.ToArray();
    }

    private static string ReadText(string resPath)
    {
        using FileAccess file = FileAccess.Open(resPath, FileAccess.ModeFlags.Read);
        return file.GetAsText();
    }

    private static Color ParseColor(string hex)
    {
        try
        {
            return new Color(hex);
        }
        catch
        {
            return Colors.Gray;
        }
    }
}
