using System;
using System.Collections.Generic;
using Godot;

namespace GrandStrategy.Core;

public enum GameState
{
    Boot,
    MainMenu,
    NewGameSetup,
    Playing,
    Paused,
    Loading,
    Saving,
}

/// <summary>Параметры новой партии (настраиваются на экране новой игры, M4).</summary>
public sealed record NewGameOptions
{
    public int StartYear { get; init; } = GameConstants.DefaultStartYear;
    public int PlayerCountryId { get; init; }
    public long Seed { get; init; }
    public string Difficulty { get; init; } = "normal";
    public bool Ironman { get; init; }
    public int GameSpeed { get; init; } = 1;
    public bool HistoricalMode { get; init; }
}

/// <summary>
/// Синглтон игры. Оркестрирует жизненный цикл партии: новая игра, конец хода,
/// пауза/возобновление, сохранение/загрузка. Рассылает события через EventBus.
/// Autoload #7.
/// </summary>
public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; } = null!;

    public GameState State { get; private set; } = GameState.Boot;
    public NewGameOptions? ActiveOptions { get; private set; }

    public override void _Ready() => Instance = this;

    public void StartNewGame(NewGameOptions options)
    {
        ActiveOptions = options;
        TimeManager.Instance.StartNewGame(options.StartYear);
        State = GameState.Playing;

        LogService.Instance.Info($"GameManager: new game (seed={options.Seed}, player={options.PlayerCountryId}, difficulty={options.Difficulty})");
        EventBus.Instance.EmitGameStarted();
        EventBus.Instance.EmitTurnStarted(0);
    }

    public void EndTurn()
    {
        if (State != GameState.Playing)
            return;

        int turn = TimeManager.Instance.CurrentTurn;
        EventBus.Instance.EmitTurnEnded(turn);

        // M5: здесь выполняется фиксированный порядок тиков экономики/населения/торговли.

        int next = TimeManager.Instance.AdvanceTurn();
        EventBus.Instance.EmitTurnStarted(next);
    }

    public void Pause()
    {
        if (State == GameState.Playing)
            State = GameState.Paused;
        TimeManager.Instance.Pause();
    }

    public void Resume()
    {
        if (State == GameState.Paused)
            State = GameState.Playing;
        TimeManager.Instance.Resume();
    }

    public void SaveGame(string slotName)
    {
        State = GameState.Saving;
        SaveManager.Instance.SaveGame(slotName, BuildSnapshot());
        State = GameState.Playing;
        EventBus.Instance.EmitGameSaved();
    }

    public void AutoSave() =>
        SaveManager.Instance.AutoSave(BuildSnapshot());

    public bool LoadGame(string slotName)
    {
        GameSnapshot? snapshot = SaveManager.Instance.LoadGame(slotName);
        if (snapshot == null)
            return false;

        ApplySnapshot(snapshot);
        EventBus.Instance.EmitGameLoaded();
        EventBus.Instance.EmitTurnStarted(TimeManager.Instance.CurrentTurn);
        return true;
    }

    private GameSnapshot BuildSnapshot()
    {
        return new GameSnapshot
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            CurrentDate = TimeManager.Instance.CurrentDateString,
            CurrentTurn = TimeManager.Instance.CurrentTurn,
            PlayerCountryId = ActiveOptions?.PlayerCountryId ?? 0,
            Seed = ActiveOptions?.Seed ?? 0,
            Difficulty = ActiveOptions?.Difficulty ?? "normal",
            Ironman = ActiveOptions?.Ironman ?? false,
            Settings = SettingsManager.Instance.Current,
            WorldState = new Dictionary<string, object?>(),
        };
    }

    private void ApplySnapshot(GameSnapshot snapshot)
    {
        ActiveOptions = new NewGameOptions
        {
            StartYear = TimeManager.Instance.CurrentYear,
            PlayerCountryId = snapshot.PlayerCountryId,
            Seed = snapshot.Seed,
            Difficulty = snapshot.Difficulty,
            Ironman = snapshot.Ironman,
        };

        // Восстановление мира (province/country/economy/...) — M9.
        TimeManager.Instance.StartNewGame(TimeManager.Instance.CurrentYear);
        while (TimeManager.Instance.CurrentTurn < snapshot.CurrentTurn)
            TimeManager.Instance.AdvanceTurn();

        State = GameState.Playing;
    }
}
