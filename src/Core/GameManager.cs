using System;
using System.Collections.Generic;
using Godot;
using GrandStrategy.AI;
using GrandStrategy.Data;
using GrandStrategy.Systems.Diplomacy;
using GrandStrategy.Systems.Economy;
using GrandStrategy.Systems.Events;
using GrandStrategy.Systems.Governance;
using GrandStrategy.Systems.Military;
using GrandStrategy.Systems.Tech;
using GrandStrategy.Utils;

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

        // Сброс и сидирование всех симуляционных систем.
        EconomyManager.Instance.Reset();
        DiplomacyManager.Instance.Reset();
        MilitaryManager.Instance.SetSeed(options.Seed);
        MilitaryManager.Instance.LoadGenerals();
        EventManager.Instance.SetSeed(options.Seed);
        AIDirector.Instance.SetSeed(options.Seed);
        AIDirector.Instance.Reset();
        TechManager.Instance.Reset();

        // Помечаем игрока.
        if (DataManager.Instance.IsLoaded)
        {
            for (int i = 0; i < DataManager.Instance.World.Countries.Length; i++)
                if (DataManager.Instance.World.Countries[i] != null)
                    DataManager.Instance.World.Countries[i].IsPlayer = (i == options.PlayerCountryId);
        }

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

        // Фиксированный порядок симуляции хода.
        EconomyManager.Instance.Tick();      // демография, экономика, торговля
        DiplomacyManager.Instance.Tick();    // отношения, военное истощение
        MilitaryManager.Instance.Tick();     // движение, бой, оккупация, снабжение
        TechManager.Instance.Tick();         // исследования
        GovernanceSystem.Tick(DataManager.Instance.World,
            new Rng((ActiveOptions?.Seed ?? 0) + turn)); // перевороты, мятежи
        EventManager.Instance.Tick();        // события
        AIDirector.Instance.Tick();          // решения ИИ

        int next = TimeManager.Instance.AdvanceTurn();

        // Автосейв по расписанию (каждые 5 ходов).
        if (SettingsManager.Instance.Current.Game.Autosave && next % 5 == 0)
            AutoSave();

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
        WorldData world = DataManager.Instance.World;
        WorldStateSaveDto worldState = WorldSaveCodec.Encode(world, EconomyManager.Instance.Economy,
            MilitaryManager.Instance.Armies, DiplomacyManager.Instance.Wars);

        return new GameSnapshot
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            CurrentDate = TimeManager.Instance.CurrentDateString,
            CurrentYear = TimeManager.Instance.CurrentYear,
            CurrentTurn = TimeManager.Instance.CurrentTurn,
            PlayerCountryId = ActiveOptions?.PlayerCountryId ?? 0,
            Seed = ActiveOptions?.Seed ?? 0,
            Difficulty = ActiveOptions?.Difficulty ?? "normal",
            Ironman = ActiveOptions?.Ironman ?? false,
            Settings = SettingsManager.Instance.Current,
            WorldState = worldState,
            ResearchedTechs = TechManager.Instance.GetResearchedForSave(),
            TechProgress = TechManager.Instance.GetProgressForSave(),
        };
    }

    private void ApplySnapshot(GameSnapshot snapshot)
    {
        ActiveOptions = new NewGameOptions
        {
            StartYear = snapshot.CurrentYear,
            PlayerCountryId = snapshot.PlayerCountryId,
            Seed = snapshot.Seed,
            Difficulty = snapshot.Difficulty,
            Ironman = snapshot.Ironman,
        };

        // Восстановление мира.
        if (snapshot.WorldState != null)
        {
            var armies = new List<ArmyData>();
            var wars = new List<WarData>();
            WorldSaveCodec.Apply(DataManager.Instance.World, EconomyManager.Instance.Economy,
                snapshot.WorldState, armies, wars);
            MilitaryManager.Instance.RestoreArmies(armies);
            DiplomacyManager.Instance.RestoreWars(wars);
        }

        // Технологии.
        TechManager.Instance.RestoreResearched(snapshot.ResearchedTechs);
        TechManager.Instance.RestoreProgress(snapshot.TechProgress);

        // Время.
        TimeManager.Instance.StartNewGame(snapshot.CurrentYear);
        while (TimeManager.Instance.CurrentTurn < snapshot.CurrentTurn)
            TimeManager.Instance.AdvanceTurn();

        // Помечаем игрока.
        for (int i = 0; i < DataManager.Instance.World.Countries.Length; i++)
            if (DataManager.Instance.World.Countries[i] != null)
                DataManager.Instance.World.Countries[i].IsPlayer = (i == snapshot.PlayerCountryId);

        State = GameState.Playing;
    }
}
