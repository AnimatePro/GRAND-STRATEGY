using System.Collections.Generic;

namespace GrandStrategy.Data;

/// <summary>Боевая роль юнита (для реалистичных взаимодействий родов войск).</summary>
public enum UnitRole
{
    Infantry = 0,
    Mobile = 1,     // кавалерия (до 1900) / танки (с 1900)
    Artillery = 2,
    AirDefense = 3, // ПВО — сбивает авиацию и дроны
    Drone = 4,      // дроны — разведка + удар
    AirForce = 5,   // авиация — удар, уязвима к ПВО
    Navy = 6,       // флот — снижает штраф десанта, морское снабжение
}

/// <summary>Тип юнита (базовые боевые характеристики).</summary>
public struct UnitTypeData
{
    public UnitTypeData() { } // CS8983

    public int Id;
    public string NameKey;
    public UnitRole Role;
    public string IconPath;     // путь к спрайту (res://assets/units/*.png)
    public float Attack;
    public float Defense;
    public float Mobility;
    public double Cost;      // стоимость найма
    public double Upkeep;    // содержание за ход
    public int ManpowerCost; // рекрутов (из male_adults)
}

/// <summary>Армия — стек юнитов в провинции.</summary>
public sealed class ArmyData
{
    public int Id;
    public int OwnerId;
    public int ProvinceId;
    public Dictionary<int, int> UnitCounts = new(); // unitTypeId -> кол-во
    public float Strength = 1f;     // 0..1
    public float Morale = 1f;       // 0..1
    public float Organization = 1f; // 0..1
    public double Supply = 100.0;   // запас снабжения
    public int CommanderId = -1;
    public int FortLevel;
    public List<int> MoveOrder = new(); // очередь провинций пути (следующая — первая)
    public int NavalLandingTurns;       // >0 = армия только что высадилась с моря (штраф в бою)
    public float MoveProgress;          // 0..1 — прогресс перехода между провинциями (анимация)

    public int TotalUnits
    {
        get
        {
            int s = 0;
            foreach (int v in UnitCounts.Values)
                s += v;
            return s;
        }
    }

    public float AttackPower(UnitTypeData[] types)
    {
        float p = 0f;
        foreach (KeyValuePair<int, int> kv in UnitCounts)
            if (kv.Key >= 0 && kv.Key < types.Length)
                p += types[kv.Key].Attack * kv.Value;
        return p;
    }

    public float DefensePower(UnitTypeData[] types)
    {
        float p = 0f;
        foreach (KeyValuePair<int, int> kv in UnitCounts)
            if (kv.Key >= 0 && kv.Key < types.Length)
                p += types[kv.Key].Defense * kv.Value;
        return p;
    }

    public int Manpower
    {
        get
        {
            int s = 0;
            foreach (int v in UnitCounts.Values)
                s += v;
            return s * 100; // 1 юнит = 100 человек (масштаб)
        }
    }
}

/// <summary>Командир/генерал — даёт бонус атаки/обороны назначенной армии.</summary>
public sealed class CommanderData
{
    public int Id;
    public int OwnerId;
    public string Name = string.Empty; // отображаемое имя
    public float Skill;                // 1..5
}

/// <summary>Запись о сражении (для тактического экрана/журнала боёв).</summary>
public sealed class BattleRecord
{
    public int Turn;
    public int ProvinceId;
    public int AttackerId;
    public int DefenderId;
    public bool AttackerWon;
    public int AttackerLosses;
    public int DefenderLosses;
    public double AttackerPower;
    public double DefenderPower;
    public Terrain Terrain;       // террейн провинции (фактор обороны)
    public float AttackerMorale;  // мораль после боя
    public float DefenderMorale;
    public bool NavalLanding;     // был штраф десанта
    public bool AirDefenseActive; // работало ПВО
}

/// <summary>Война между двумя странами.</summary>
public sealed class WarData
{
    public int Id;
    public int AttackerId;
    public int DefenderId;
    public List<int> AllyIds = new();      // союзники защищающегося
    public List<int> AttackerAllies = new(); // союзники атакующего
    public string WarGoals = string.Empty;
    public int StartTurn;
    public int Battles;
    public List<int> OccupiedProvinces = new();
    public double WarScore; // -100..100 (с точки зрения атакующего)
}
