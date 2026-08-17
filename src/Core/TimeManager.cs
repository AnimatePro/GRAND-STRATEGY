using System;
using Godot;

namespace GrandStrategy.Core;

/// <summary>
/// Синглтон времени. Гибридная модель ход/тик: экономика и симуляция считаются по ходам
/// (фиксированный порядок тиков — см. EconomyManager в M5), здесь — счётчик ходов, дата,
/// пауза и скорости 0..5. _Process не используется — время движется только вызовами AdvanceTurn.
/// Autoload #6.
/// </summary>
public partial class TimeManager : Node
{
    public static TimeManager Instance { get; private set; } = null!;

    public const int MinSpeed = 0;
    public const int MaxSpeed = 5;

    public int CurrentTurn { get; private set; } = 0;
    public int CurrentYear { get; private set; } = GameConstants.DefaultStartYear;
    public int DayOfYear { get; private set; } = 1;
    public bool IsPaused { get; private set; } = true;
    public int Speed { get; private set; } = 0;

    public bool IsRunning => !IsPaused && Speed > 0;

    public override void _Ready() => Instance = this;

    public void StartNewGame(int startYear, int dayOfYear = 1)
    {
        CurrentTurn = 0;
        CurrentYear = startYear;
        DayOfYear = dayOfYear;
        IsPaused = false;
        Speed = 1;
        LogService.Instance.Debug($"TimeManager: new game started at year {startYear}");
    }

    public void Pause() => IsPaused = true;

    public void Resume() => IsPaused = false;

    public void TogglePause() => IsPaused = !IsPaused;

    public void SetSpeed(int speed) => Speed = Math.Clamp(speed, MinSpeed, MaxSpeed);

    /// <summary>Продвигает время на один ход, возвращает новый номер хода.</summary>
    public int AdvanceTurn()
    {
        CurrentTurn++;
        AdvanceDate(GameConstants.DaysPerTurn);
        return CurrentTurn;
    }

    public string CurrentDateString => $"{DayOfYear:000}.{CurrentYear:0000}";

    private void AdvanceDate(int days)
    {
        DayOfYear += days;
        while (DayOfYear > 365)
        {
            DayOfYear -= 365;
            CurrentYear++;
        }
    }
}
