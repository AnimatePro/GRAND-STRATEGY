using System;
using System.Collections.Generic;
using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;
using GrandStrategy.Systems.Diplomacy;
using GrandStrategy.Systems.Tech;
using GrandStrategy.SimCore;
using GrandStrategy.Utils;

namespace GrandStrategy.Systems.Military;

/// <summary>
/// Менеджер военных действий (Autoload #12). Армии-стеки, перемещение по провинциям
/// (BFS по графу соседства), детерминированный бой (атака/оборона/террейн/форт/мораль),
/// снабжение и истощение, оккупация провинций. Рекруты берутся из male_adults.
/// </summary>
public partial class MilitaryManager : Node
{
    public static MilitaryManager Instance { get; private set; } = null!;

    // --- Юниты по эпохам (реалистичные роды войск для каждого периода) ---
    // До 1900 — пехота/кавалерия/артиллерия; с 1900 — пехота/танки/артиллерия/
    // ПВО/дроны/авиация/флот. Никаких анахронизмов между эпохами.

    public static readonly UnitTypeData[] ClassicUnits =
    {
        new() { Id = 0, NameKey = "UNIT_INFANTRY", Role = UnitRole.Infantry, Attack = 1.0f, Defense = 2.0f, Mobility = 1f, Cost = 10, Upkeep = 0.2, ManpowerCost = 100 },
        new() { Id = 1, NameKey = "UNIT_CAVALRY", Role = UnitRole.Mobile, Attack = 2.0f, Defense = 1.0f, Mobility = 2f, Cost = 20, Upkeep = 0.4, ManpowerCost = 100 },
        new() { Id = 2, NameKey = "UNIT_ARTILLERY", Role = UnitRole.Artillery, Attack = 3.0f, Defense = 1.5f, Mobility = 1f, Cost = 40, Upkeep = 0.8, ManpowerCost = 100 },
    };

    public static readonly UnitTypeData[] ModernUnits =
    {
        new() { Id = 0, NameKey = "UNIT_INFANTRY", Role = UnitRole.Infantry, Attack = 1.0f, Defense = 2.0f, Mobility = 1f, Cost = 10, Upkeep = 0.2, ManpowerCost = 100 },
        new() { Id = 1, NameKey = "UNIT_ARMOR", Role = UnitRole.Mobile, Attack = 2.5f, Defense = 1.5f, Mobility = 1.5f, Cost = 30, Upkeep = 0.6, ManpowerCost = 100 },
        new() { Id = 2, NameKey = "UNIT_ARTILLERY", Role = UnitRole.Artillery, Attack = 3.0f, Defense = 1.5f, Mobility = 1f, Cost = 40, Upkeep = 0.8, ManpowerCost = 100 },
        new() { Id = 3, NameKey = "UNIT_AIR_DEFENSE", Role = UnitRole.AirDefense, Attack = 1.5f, Defense = 2.5f, Mobility = 1f, Cost = 35, Upkeep = 0.7, ManpowerCost = 80 },
        new() { Id = 4, NameKey = "UNIT_DRONE", Role = UnitRole.Drone, Attack = 2.2f, Defense = 0.5f, Mobility = 1.5f, Cost = 25, Upkeep = 0.4, ManpowerCost = 40 },
        new() { Id = 5, NameKey = "UNIT_AIR_FORCE", Role = UnitRole.AirForce, Attack = 3.5f, Defense = 0.8f, Mobility = 2f, Cost = 50, Upkeep = 1.0, ManpowerCost = 60 },
        new() { Id = 6, NameKey = "UNIT_NAVY", Role = UnitRole.Navy, Attack = 1.8f, Defense = 2.0f, Mobility = 2f, Cost = 45, Upkeep = 0.9, ManpowerCost = 120 },
    };

    /// <summary>Юниты текущей эпохи (по игровому году).</summary>
    public static UnitTypeData[] UnitTypes
    {
        get
        {
            int year = TimeManager.Instance != null ? TimeManager.Instance.CurrentYear : GameConstants.DefaultStartYear;
            return year >= 1900 ? ModernUnits : ClassicUnits;
        }
    }

    /// <summary>Множество юнитов для конкретного года (для тестов/прогноза).</summary>
    public static UnitTypeData[] UnitsForYear(int year) => year >= 1900 ? ModernUnits : ClassicUnits;

    // --- Реальные генералы по странам и эпохам (data/generals.csv) ---
    private readonly Dictionary<string, List<string>> _generalsByCode = new(StringComparer.Ordinal);

    /// <summary>Загрузка реальных генералов; вызывается при загрузке данных.</summary>
    public void LoadGenerals()
    {
        _generalsByCode.Clear();
        foreach (Dictionary<string, string> row in CsvTableLoader.Load("res://data/generals.csv"))
        {
            string code = CsvTableLoader.Str(row, "code");
            string era = CsvTableLoader.Str(row, "era");
            string generals = CsvTableLoader.Str(row, "generals");
            if (code.Length == 0 || generals.Length == 0)
                continue;
            string key = code + ":" + era;
            var names = new List<string>();
            foreach (string n in generals.Split(';'))
                if (!string.IsNullOrWhiteSpace(n))
                    names.Add(n.Trim());
            _generalsByCode[key] = names;
        }
    }

    /// <summary>Эпоха для текущего года (ключ, совпадающий с generals.csv).</summary>
    private static string EraForYear(int year)
    {
        return year >= 2000 ? "2024" : "1936";
    }

    // Фолбэк-пулы (если для страны нет реального генерала в эпохе).
    private static readonly string[] ClassicCommanders =
    {
        "Napoleon", "Wellington", "Kutuzov", "Blucher", "Suworow", "Nelson", "Ney", "Bagration",
    };
    private static readonly string[] ModernCommanders =
    {
        "Zhukov", "Rommel", "Patton", "Eisenhower", "Montgomery", "Guderian",
        "MacArthur", "Manstein", "Schwarzkopf", "Petraeus", "Zaluzhnyi", "Grant",
    };

    public List<ArmyData> Armies { get; private set; } = new();
    public List<CommanderData> Commanders { get; private set; } = new();

    /// <summary>Выбранная игроком армия для приказа движения (-1 = не выбрана).</summary>
    public int SelectedArmyId = -1;

    /// <summary>Макробилдер: id здания для массовой застройки кликом (-1 = выкл).</summary>
    public int MacroBuildId = -1;

    /// <summary>Макробилдер рекрутинга: id шаблона для массового найма кликом (-1 = выкл).</summary>
    public int MacroRecruitTemplate = -1;

    // --- Шаблоны армий (id = индекс; словарь юнит -> количество) ---
    // Слоты: 0 пехота, 1 мобильные, 2 артиллерия, 3 ПВО, 4 дроны, 5 авиация, 6 флот.
    public static readonly Dictionary<int, int>[] Templates =
    {
        new() { { 0, 5 } },                             // 0: Пехота
        new() { { 0, 4 }, { 1, 2 } },                   // 1: Пехота + мобильные
        new() { { 0, 6 }, { 2, 3 } },                   // 2: Пехота + артиллерия
        new() { { 0, 4 }, { 1, 2 }, { 2, 2 } },         // 3: Смешанная (классика)
        new() { { 0, 5 }, { 2, 2 }, { 3, 1 }, { 4, 2 } }, // 4: Современная общевойсковая
    };

    public static readonly string[] TemplateNames =
    {
        "TPL_INFANTRY", "TPL_MOBILE", "TPL_ART", "TPL_MIXED", "TPL_MODERN",
    };

    private int _nextArmyId = 1;
    private int _nextCommanderId = 1;
    private long _seed;

    public override void _Ready()
    {
        Instance = this;
        EventBus.Instance.GameStarted += OnGameStarted;
    }

    private void OnGameStarted()
    {
        Armies.Clear();
        Commanders.Clear();
        _nextArmyId = 1;
        _nextCommanderId = 1;
    }

    public void SetSeed(long seed) => _seed = seed;

    /// <summary>Восстановление армий из сохранения.</summary>
    public void RestoreArmies(List<ArmyData> armies)
    {
        Armies.Clear();
        Armies.AddRange(armies);
        _nextArmyId = 1;
        foreach (ArmyData a in Armies)
            _nextArmyId = Math.Max(_nextArmyId, a.Id + 1);
    }

    // --- Набор и расформирование ---------------------------------------------

    public bool RecruitArmy(int ownerId, int provinceId, Dictionary<int, int> unitCounts)
    {
        WorldData world = DataManager.Instance.World;
        if (!world.TryGetProvince(provinceId, out ProvinceData p))
            return false;
        if (p.EffectiveOwnerId != ownerId)
            return false;

        int manpower = 0;
        foreach (KeyValuePair<int, int> kv in unitCounts)
            manpower += kv.Value * UnitTypes[kv.Key].ManpowerCost;
        if (!ConsumeManpower(world, ownerId, manpower))
            return false;

        var army = new ArmyData
        {
            Id = _nextArmyId++,
            OwnerId = ownerId,
            ProvinceId = provinceId,
            UnitCounts = unitCounts,
        };
        Armies.Add(army);
        LogService.Instance.Info($"Military: army {army.Id} recruited by {ownerId} in {provinceId}");
        return true;
    }

    public void DisbandArmy(int armyId)
    {
        Armies.RemoveAll(a => a.Id == armyId);
    }

    // --- Командиры -----------------------------------------------------------

    /// <summary>Найм командира (случайный навык 1..5), стоимость растёт с числом командиров.</summary>
    public bool RecruitCommander(int ownerId, int provinceId)
    {
        WorldData world = DataManager.Instance.World;
        CountryData c = world.GetCountry(ownerId);
        if (c == null)
            return false;
        double cost = 1000 + Commanders.Count * 500;
        if (c.Treasury < cost)
            return false;

        c.Treasury -= cost;
        var rng = new Rng(_seed + _nextCommanderId * 7919L + TimeManager.Instance.CurrentTurn);

        // Реальный генерал страны в текущей эпохе; иначе — фолбэк-пул эпохи.
        string name;
        string key = c.Code + ":" + EraForYear(TimeManager.Instance.CurrentYear);
        if (_generalsByCode.TryGetValue(key, out List<string>? countryGenerals) && countryGenerals.Count > 0)
        {
            name = countryGenerals[rng.NextInt(0, countryGenerals.Count - 1)];
        }
        else
        {
            string[] pool = TimeManager.Instance.CurrentYear >= 1900 ? ModernCommanders : ClassicCommanders;
            name = pool[rng.NextInt(0, pool.Length - 1)];
        }

        var commander = new CommanderData
        {
            Id = _nextCommanderId++,
            OwnerId = ownerId,
            Name = name,
            Skill = rng.NextInt(1, 5),
        };
        Commanders.Add(commander);
        // Автоназначение первой армии без командира.
        ArmyData? free = Armies.Find(a => a.OwnerId == ownerId && a.CommanderId < 0);
        if (free != null)
            free.CommanderId = commander.Id;

        LogService.Instance.Info($"Military: commander {commander.Name} (skill {commander.Skill}) recruited by {ownerId}");
        return true;
    }

    /// <summary>Назначение командира армии (null = снять).</summary>
    public bool AssignCommander(int armyId, int commanderId)
    {
        ArmyData? army = Armies.Find(a => a.Id == armyId);
        CommanderData? commander = Commanders.Find(c => c.Id == commanderId);
        if (army == null)
            return false;
        if (commanderId >= 0 && (commander == null || commander.OwnerId != army.OwnerId))
            return false;
        army.CommanderId = commanderId;
        return true;
    }

    /// <summary>Множитель командира армии (1.0 без командира).</summary>
    public static double CommanderMult(ArmyData army)
    {
        if (army.CommanderId < 0)
            return 1.0;
        CommanderData? c = Instance.Commanders.Find(x => x.Id == army.CommanderId);
        return c != null ? 1.0 + c.Skill * 0.08 : 1.0;
    }

    private static bool ConsumeManpower(WorldData world, int ownerId, int amount)
    {
        int remaining = amount;
        foreach (int pid in world.Countries[ownerId].OwnedProvinceIds)
        {
            int idx = world.ProvinceIdToIndex[pid];
            ProvinceData p = world.Provinces[idx];
            int take = Math.Min(p.MaleAdults, remaining);
            p.MaleAdults -= take;
            remaining -= take;
            world.Provinces[idx] = p;
            if (remaining <= 0)
                return true;
        }
        return remaining <= 0;
    }

    // --- Перемещение ---------------------------------------------------------

    public bool OrderMove(int armyId, int targetProvinceId)
    {
        ArmyData? army = Armies.Find(a => a.Id == armyId);
        if (army == null)
            return false;

        WorldData world = DataManager.Instance.World;
        List<int>? path = Pathfind(world, army.ProvinceId, targetProvinceId);
        if (path == null || path.Count <= 1)
            return false;

        // Первый элемент — текущая провинция, пропускаем; остальное — очередь пути.
        army.MoveOrder.Clear();
        for (int i = 1; i < path.Count; i++)
            army.MoveOrder.Add(path[i]);
        return true;
    }

    private static List<int>? Pathfind(WorldData world, int from, int to)
    {
        if (from == to)
            return new List<int> { from };

        var prev = new Dictionary<int, int>();
        var queue = new Queue<int>();
        var visited = new HashSet<int> { from };
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            if (cur == to)
                break;
            ProvinceData p = world.GetProvince(cur);
            foreach (int nid in p.NeighborIds)
            {
                if (visited.Add(nid))
                {
                    prev[nid] = cur;
                    queue.Enqueue(nid);
                }
            }

            // Морская переброска: из прибрежной провинции — в любую другую прибрежную.
            if (p.IsCoastal)
            {
                foreach (int cid in CoastalProvinces(world))
                {
                    if (cid != cur && visited.Add(cid))
                    {
                        prev[cid] = cur;
                        queue.Enqueue(cid);
                    }
                }
            }
        }

        if (!prev.ContainsKey(to))
            return null;

        var path = new List<int> { to };
        int step = to;
        while (prev.TryGetValue(step, out int parent))
        {
            path.Add(parent);
            step = parent;
        }
        path.Reverse();
        return path;
    }

    private static int[]? _coastalCache;
    private static int _coastalCacheWorld;

    private static int[] CoastalProvinces(WorldData world)
    {
        if (_coastalCache != null && _coastalCacheWorld == world.ProvinceCount)
            return _coastalCache;

        var list = new List<int>();
        for (int i = 0; i < world.ProvinceCount; i++)
            if (world.Provinces[i].IsCoastal)
                list.Add(i);
        _coastalCache = list.ToArray();
        _coastalCacheWorld = world.ProvinceCount;
        return _coastalCache;
    }

    // --- Тик (движение, бой, оккупация, снабжение) ---------------------------

    public void Tick()
    {
        WorldData world = DataManager.Instance.World;
        var rng = new Rng(_seed + TimeManager.Instance.CurrentTurn);

        // 1. Морской перехват и движение (десанты атакуются вражескими флотами).
        foreach (ArmyData army in Armies)
            MoveOneStep(world, army);

        // 2. Бой: враждебные армии в одной провинции (в т.ч. флоты).
        ResolveBattles(world, rng);

        // 3. Морские сражения: вражеские флоты в соседних прибрежных провинциях.
        ResolveNavalBattles(world, rng);

        // 4. Оккупация.
        ApplyOccupation(world);

        // 5. Снабжение и истощение.
        ApplySupply(world);

        // 6. Удаление уничтоженных армий.
        Armies.RemoveAll(a => a.TotalUnits <= 0);
    }

    /// <summary>Флоты противников в смежных прибрежных провинциях вступают в морской бой.</summary>
    private void ResolveNavalBattles(WorldData world, Rng rng)
    {
        var fleets = new List<ArmyData>();
        foreach (ArmyData army in Armies)
        {
            if (IsFleet(army))
                fleets.Add(army);
        }

        for (int i = 0; i < fleets.Count; i++)
        {
            for (int j = i + 1; j < fleets.Count; j++)
            {
                ArmyData a = fleets[i];
                ArmyData b = fleets[j];
                if (a.OwnerId == b.OwnerId)
                    continue;
                if (!DiplomacyManager.Instance.AreAtWar(a.OwnerId, b.OwnerId))
                    continue;
                if (!ProvincesAdjacentOrCoastal(world, a.ProvinceId, b.ProvinceId))
                    continue;
                ResolveNavalCombat(world, a, b);
            }
        }
    }

    /// <summary>Морской бой: участвуют только флот и авиация (ПВО/пехота — слабоэффективны в море).</summary>
    private void ResolveNavalCombat(WorldData world, ArmyData a, ArmyData b)
    {
        double aP = RolePower(a, UnitRole.Navy) * 1.0
            + RolePower(a, UnitRole.AirForce) * 0.8
            + RolePower(a, UnitRole.Drone) * 0.4;
        double bP = RolePower(b, UnitRole.Navy) * 1.0
            + RolePower(b, UnitRole.AirForce) * 0.8
            + RolePower(b, UnitRole.Drone) * 0.4;

        double total = aP + bP;
        if (total <= 0)
            return;

        double aWin = aP / total;
        // Потери преимущественно в кораблях/авиации.
        ApplyRoleCasualties(a, aWin < 0.5 ? (1.0 - aWin) * 2.0 * 0.3 : 0.1);
        ApplyRoleCasualties(b, aWin > 0.5 ? aWin * 2.0 * 0.3 : 0.1);

        a.Morale = Mathf.Clamp(a.Morale - 0.1f, 0f, 1f);
        b.Morale = Mathf.Clamp(b.Morale - 0.1f, 0f, 1f);

        WarData? war = DiplomacyManager.Instance.FindWar(a.OwnerId, b.OwnerId);
        if (war != null)
            war.WarScore = Mathf.Clamp((float)(war.WarScore + (aWin - 0.5) * 1.5), -100f, 100f);
    }

    /// <summary>Потери в роли юнита (доля потерь применяется к морским/воздушным юнитам).</summary>
    private static void ApplyRoleCasualties(ArmyData army, double lossShare)
    {
        lossShare = Math.Clamp(lossShare, 0.0, 0.8);
        var keys = new List<int>(army.UnitCounts.Keys);
        foreach (int k in keys)
        {
            if (k < 0 || k >= UnitTypes.Length)
                continue;
            UnitRole role = UnitTypes[k].Role;
            if (role is UnitRole.Navy or UnitRole.AirForce or UnitRole.Drone)
            {
                army.UnitCounts[k] = (int)(army.UnitCounts[k] * (1.0 - lossShare));
                if (army.UnitCounts[k] <= 0)
                    army.UnitCounts.Remove(k);
            }
        }
    }

    /// <summary>Армия считается флотом, если большинство её силы — флот.</summary>
    private static bool IsFleet(ArmyData army) =>
        RolePower(army, UnitRole.Navy) > RolePower(army, UnitRole.Infantry) + RolePower(army, UnitRole.Mobile);

    /// <summary>Две провинции «соседствуют по морю», если обе прибрежные (флоты могут сойтись).</summary>
    private static bool ProvincesAdjacentOrCoastal(WorldData world, int pa, int pb)
    {
        ProvinceData a = world.GetProvince(pa);
        ProvinceData b = world.GetProvince(pb);
        if (System.Array.IndexOf(a.NeighborIds, pb) >= 0)
            return true;
        return a.IsCoastal && b.IsCoastal;
    }

    private void MoveOneStep(WorldData world, ArmyData army)
    {
        if (army.MoveOrder.Count == 0)
        {
            army.MoveProgress = 0f;
            return;
        }

        // Прогресс движения: армия переходит в следующую провинцию за несколько тиков.
        army.MoveProgress += 1f / Math.Max(MoveTicksPerProvince, 1);
        if (army.MoveProgress < 1f)
            return;

        army.MoveProgress = 0f;
        int next = army.MoveOrder[0];
        // Блокируем движение в провинцию с враждебной армией.
        if (HasHostileArmy(world, next, army.OwnerId))
            return;

        // Морской переход = цель не является сухопутным соседом (переброска по морю).
        bool isSeaJump = System.Array.IndexOf(world.GetProvince(army.ProvinceId).NeighborIds, next) < 0;

        // Морской перехват: вражеские флоты атакуют десант при переправе.
        if (isSeaJump)
            ApplyNavalInterception(world, army);

        army.ProvinceId = next;
        army.MoveOrder.RemoveAt(0);

        if (isSeaJump)
            army.NavalLandingTurns = 2; // штраф десанта на 2 хода
    }

    /// <summary>
    /// Вражеские флоты в прибрежных провинциях перехватывают морской десант.
    /// Собственный эскорт (флот атакующего) снижает потери.
    /// </summary>
    private void ApplyNavalInterception(WorldData world, ArmyData army)
    {
        double enemyNaval = 0.0;
        foreach (ArmyData other in Armies)
        {
            if (other.OwnerId == army.OwnerId || other.Id == army.Id)
                continue;
            if (!DiplomacyManager.Instance.AreAtWar(army.OwnerId, other.OwnerId))
                continue;
            ProvinceData op = world.GetProvince(other.ProvinceId);
            if (!op.IsCoastal)
                continue;
            enemyNaval += RolePower(other, UnitRole.Navy) + RolePower(other, UnitRole.AirForce) * 0.5;
        }

        if (enemyNaval <= 0)
            return;

        double escort = RolePower(army, UnitRole.Navy) * 3.0;
        double defense = escort + army.TotalUnits * 0.2;
        double ratio = enemyNaval / Math.Max(defense, 1.0);
        double lossShare = Mathf.Clamp((float)ratio, 0f, 0.5f);
        ApplyCasualties(army, (int)(army.TotalUnits * lossShare));

        if (lossShare > 0.02)
            LogService.Instance.Info($"Naval interception: army {army.Id} lost {(lossShare * 100):0}% crossing sea");
    }

    /// <summary>Тиков на переход в соседнюю провинцию.</summary>
    public const float MoveTicksPerProvince = 2f;

    private void ResolveBattles(WorldData world, Rng rng)
    {
        // Группировка по провинциям.
        var byProvince = new Dictionary<int, List<ArmyData>>();
        foreach (ArmyData army in Armies)
        {
            if (!byProvince.TryGetValue(army.ProvinceId, out List<ArmyData>? list))
                byProvince[army.ProvinceId] = list = new List<ArmyData>();
            list.Add(army);
        }

        foreach (KeyValuePair<int, List<ArmyData>> kv in byProvince)
        {
            List<ArmyData> armies = kv.Value;
            for (int i = 0; i < armies.Count; i++)
            {
                for (int j = i + 1; j < armies.Count; j++)
                {
                    if (DiplomacyManager.Instance.AreAtWar(armies[i].OwnerId, armies[j].OwnerId))
                        ResolveCombat(world, armies[i], armies[j], rng);
                }
            }
        }
    }

    private void ResolveCombat(WorldData world, ArmyData a, ArmyData b, Rng rng)
    {
        ProvinceData province = world.GetProvince(a.ProvinceId);
        double terrainDef = TerrainDefenseBonus(province.Terrain);
        double fortDef = b.FortLevel * 0.05;

        // Командиры и штраф морского десанта (атакующая сторона — a).
        double aCommander = CommanderMult(a);
        double bCommander = CommanderMult(b);
        double aLanding = a.NavalLandingTurns > 0 ? 0.7 : 1.0;

        // Ролевые взаимодействия родов войск (реалистично):
        // - флот атакующего снимает штраф десанта;
        // - ПВО обороняющегося сбивает авиацию/дроны атакующего;
        // - дроны дают разведку (бонус атаки).
        double aAirPower = RolePower(a, UnitRole.AirForce) + RolePower(a, UnitRole.Drone);
        double bAirDefense = RolePower(b, UnitRole.AirDefense);
        double aDroneRecon = RolePower(a, UnitRole.Drone);
        bool aHasNavy = HasRole(a, UnitRole.Navy);

        if (aHasNavy && a.NavalLandingTurns > 0)
            aLanding = 1.0; // флот обеспечивает десант без штрафа

        double airReduction = Mathf.Clamp((float)(bAirDefense / Math.Max(aAirPower, 1.0)), 0f, 0.8f);
        double airFactor = 1.0 - airReduction;
        double reconFactor = 1.0 + Mathf.Min((float)aDroneRecon * 0.02f, 0.3f);

        double attackP = a.AttackPower(UnitTypes) * a.Morale * a.Strength
            * TechManager.Instance.MilitaryMult(a.OwnerId)
            * DifficultyModifiers.MilitaryMult(a.OwnerId)
            * aCommander * aLanding * airFactor * reconFactor;
        double defenseP = b.DefensePower(UnitTypes) * b.Morale * b.Strength * (1 + terrainDef + fortDef)
            * TechManager.Instance.MilitaryMult(b.OwnerId)
            * DifficultyModifiers.MilitaryMult(b.OwnerId)
            * bCommander;

        double total = attackP + defenseP;
        if (total <= 0)
            return;

        double attackerWin = SimFormulas.CombatWinChance(attackP, defenseP);
        // Потери доли юнитов.
        ApplyCasualties(a, (int)(a.TotalUnits * (1.0 - attackerWin) * 0.3));
        ApplyCasualties(b, (int)(b.TotalUnits * attackerWin * 0.3));

        a.Morale = Mathf.Clamp(a.Morale - 0.15f, 0f, 1f);
        b.Morale = Mathf.Clamp(b.Morale - 0.15f, 0f, 1f);
        a.Strength = Mathf.Clamp(a.Strength - 0.05f, 0f, 1f);
        b.Strength = Mathf.Clamp(b.Strength - 0.05f, 0f, 1f);

        WarData? war = DiplomacyManager.Instance.FindWar(a.OwnerId, b.OwnerId);
        if (war != null)
        {
            war.Battles++;
            war.WarScore += (attackerWin - 0.5) * 2.0; // атакующий выигрывает/проигрывает
            war.WarScore = Mathf.Clamp((float)war.WarScore, -100f, 100f);
        }
    }

    /// <summary>Суммарная сила юнитов определённой роли в армии.</summary>
    private static float RolePower(ArmyData army, UnitRole role)
    {
        float p = 0f;
        foreach (KeyValuePair<int, int> kv in army.UnitCounts)
            if (kv.Key >= 0 && kv.Key < UnitTypes.Length && UnitTypes[kv.Key].Role == role)
                p += UnitTypes[kv.Key].Attack * kv.Value;
        return p;
    }

    private static bool HasRole(ArmyData army, UnitRole role)
    {
        foreach (KeyValuePair<int, int> kv in army.UnitCounts)
            if (kv.Key >= 0 && kv.Key < UnitTypes.Length && UnitTypes[kv.Key].Role == role)
                return true;
        return false;
    }

    private static void ApplyCasualties(ArmyData army, int casualties)
    {
        int total = army.TotalUnits;
        if (total <= 0 || casualties <= 0)
            return;
        double share = SimFormulas.CasualtyShare(total, casualties);
        var keys = new List<int>(army.UnitCounts.Keys);
        foreach (int k in keys)
        {
            army.UnitCounts[k] = (int)(army.UnitCounts[k] * (1.0 - share));
            if (army.UnitCounts[k] <= 0)
                army.UnitCounts.Remove(k);
        }
    }

    private readonly Dictionary<int, float> _siege = new();

    private void ApplyOccupation(WorldData world)
    {
        foreach (ArmyData army in Armies)
        {
            ProvinceData p = world.GetProvince(army.ProvinceId);
            if (p.OwnerId < 0 || p.OwnerId == army.OwnerId)
                continue;
            if (p.ControllerId == army.OwnerId)
                continue; // уже оккупировано
            if (!DiplomacyManager.Instance.AreAtWar(army.OwnerId, p.OwnerId))
                continue;
            if (HasHostileArmy(world, army.ProvinceId, army.OwnerId))
                continue; // есть защитники — идёт бой

            // Осада гарнизона провинции.
            float garrison = _siege.TryGetValue(army.ProvinceId, out float g)
                ? g : GarrisonStrength(p);
            garrison -= army.AttackPower(UnitTypes) * 0.5f;

            if (garrison <= 0f)
            {
                p.ControllerId = army.OwnerId;
                p.Unrest = Mathf.Clamp(p.Unrest + 0.2f, 0f, 1f);
                world.SetProvince(army.ProvinceId, in p);
                _siege.Remove(army.ProvinceId);

                WarData? war = DiplomacyManager.Instance.FindWar(army.OwnerId, p.OwnerId);
                if (war != null)
                {
                    if (!war.OccupiedProvinces.Contains(army.ProvinceId))
                        war.OccupiedProvinces.Add(army.ProvinceId);
                    war.WarScore = Mathf.Clamp((float)war.WarScore + 3f, -100f, 100f);
                }
                LogService.Instance.Info($"Military: province {army.ProvinceId} occupied by {army.OwnerId}");
            }
            else
            {
                _siege[army.ProvinceId] = garrison;
            }
        }

        // Сброс осад провинций, где больше нет осаждающих.
        var stale = new List<int>();
        foreach (KeyValuePair<int, float> kv in _siege)
        {
            if (!HasHostileArmy(world, kv.Key, world.GetProvince(kv.Key).OwnerId))
                stale.Add(kv.Key);
        }
        foreach (int pid in stale)
            _siege.Remove(pid);
    }

    private static float GarrisonStrength(in ProvinceData p) =>
        50f + p.FortLevel * 80f + p.Development * 60f;

    /// <summary>Колонизация пустой провинции (owner < 0) соседней страной.</summary>
    public bool Colonize(int ownerId, int provinceId)
    {
        WorldData world = DataManager.Instance.World;
        ProvinceData p = world.GetProvince(provinceId);
        if (p.OwnerId >= 0)
            return false; // уже заселена

        bool adjacent = false;
        foreach (int nid in p.NeighborIds)
        {
            ProvinceData n = world.GetProvince(nid);
            if (n.OwnerId == ownerId)
            {
                adjacent = true;
                break;
            }
        }
        if (!adjacent)
            return false;

        CountryData c = world.GetCountry(ownerId);
        double cost = 2000 + p.TotalPopulation * 0.5;
        if (c.Treasury < cost)
            return false;

        c.Treasury -= cost;
        p.OwnerId = ownerId;
        p.ControllerId = -1;
        p.Unrest = 0.3f;
        world.SetProvince(provinceId, in p);
        c.OwnedProvinceIds.Add(provinceId);
        LogService.Instance.Info($"Military: {ownerId} colonized province {provinceId}");
        return true;
    }

    /// <summary>Постройка здания в провинции (проверка цены/лимитов/побережья).</summary>
    public bool BuildBuilding(int ownerId, int provinceId, int buildingId)
    {
        WorldData world = DataManager.Instance.World;
        if (buildingId < 0 || buildingId >= world.Buildings.Length)
            return false;
        BuildingData b = world.Buildings[buildingId];
        ProvinceData p = world.GetProvince(provinceId);
        if (p.EffectiveOwnerId != ownerId)
            return false;
        if (b.RequiresCoast && !p.IsCoastal)
            return false;

        int count = 0;
        foreach (int bid in p.BuildingIds)
            if (bid == buildingId)
                count++;
        if (count >= b.MaxPerProvince)
            return false;

        CountryData c = world.GetCountry(ownerId);
        if (c == null || c.Treasury < b.BuildCost)
            return false;

        c.Treasury -= b.BuildCost;
        var list = new List<int>(p.BuildingIds) { buildingId };
        p.BuildingIds = list.ToArray();
        world.SetProvince(provinceId, in p);
        LogService.Instance.Info($"Military: {ownerId} built {b.NameKey} in province {provinceId}");
        return true;
    }

    /// <summary>Постройка форта (уровень +1, до 5).</summary>
    public bool BuildFort(int ownerId, int provinceId)
    {
        WorldData world = DataManager.Instance.World;
        ProvinceData p = world.GetProvince(provinceId);
        if (p.EffectiveOwnerId != ownerId || p.FortLevel >= 5)
            return false;
        CountryData c = world.GetCountry(ownerId);
        double cost = 350 * (p.FortLevel + 1);
        if (c.Treasury < cost)
            return false;
        c.Treasury -= cost;
        p.FortLevel++;
        world.SetProvince(provinceId, in p);
        return true;
    }

    private bool HasHostileArmy(WorldData world, int provinceId, int friendlyOwner)
    {
        foreach (ArmyData army in Armies)
            if (army.ProvinceId == provinceId &&
                DiplomacyManager.Instance.AreAtWar(army.OwnerId, friendlyOwner))
                return true;
        return false;
    }

    private void ApplySupply(WorldData world)
    {
        foreach (ArmyData army in Armies)
        {
            ProvinceData p = world.GetProvince(army.ProvinceId);
            bool friendly = p.EffectiveOwnerId == army.OwnerId;
            if (friendly)
                army.Supply = Math.Min(army.Supply + 20.0, 100.0);
            else
                army.Supply -= army.TotalUnits * 0.5;

            if (army.Supply <= 0)
            {
                army.Supply = 0;
                army.Strength = Mathf.Clamp(army.Strength - 0.05f, 0f, 1f); // истощение
            }

            // Штраф десанта спадает со временем.
            if (army.NavalLandingTurns > 0)
                army.NavalLandingTurns--;
        }
    }

    private static double TerrainDefenseBonus(Terrain t)
    {
        return t switch
        {
            Terrain.Mountains => 0.3,
            Terrain.Hills => 0.15,
            Terrain.Forest => 0.1,
            Terrain.Jungle => 0.1,
            Terrain.Marsh => 0.1,
            _ => 0.0,
        };
    }
}
