using System.Collections.Generic;

namespace GrandStrategy.Data;

/// <summary>Тип юнита (базовые боевые характеристики).</summary>
public struct UnitTypeData
{
    public int Id;
    public string NameKey;
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
