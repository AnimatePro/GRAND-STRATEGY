using Godot;

namespace GrandStrategy.Core;

/// <summary>
/// Глобальная шина событий. Все подсистемы общаются через сигналы этого синглтона,
/// что исключает жёсткие ссылки между менеджерами и позволяет UI подписываться на
/// изменения состояния вместо попиксельного опроса каждого кадра.
/// Autoload #1 — всегда доступен раньше остальных менеджеров.
/// </summary>
public partial class EventBus : Node
{
    public static EventBus Instance { get; private set; } = null!;

    public override void _Ready()
    {
        Instance = this;
    }

    // --- Lifecycle ----------------------------------------------------------
    [Signal] public delegate void GameStartedEventHandler();
    [Signal] public delegate void GameLoadedEventHandler();
    [Signal] public delegate void GameSavedEventHandler();

    // --- Turn ---------------------------------------------------------------
    [Signal] public delegate void TurnStartedEventHandler(int turn);
    [Signal] public delegate void TurnEndedEventHandler(int turn);

    // --- Selection ----------------------------------------------------------
    [Signal] public delegate void ProvinceSelectedEventHandler(int provinceId);
    [Signal] public delegate void CountrySelectedEventHandler(int countryId);

    // --- Domains ------------------------------------------------------------
    [Signal] public delegate void EconomyUpdatedEventHandler();
    [Signal] public delegate void TradeUpdatedEventHandler();
    [Signal] public delegate void DiplomacyUpdatedEventHandler();
    [Signal] public delegate void WarDeclaredEventHandler(int attackerId, int defenderId);
    [Signal] public delegate void PeaceSignedEventHandler(int countryAId, int countryBId);
    [Signal] public delegate void PopulationUpdatedEventHandler();

    // --- UI / system --------------------------------------------------------
    [Signal] public delegate void UINotificationEventHandler(string text);
    [Signal] public delegate void SettingsChangedEventHandler();
    [Signal] public delegate void LocalizationChangedEventHandler();
    [Signal] public delegate void ErrorOccurredEventHandler(string message);
    [Signal] public delegate void DataLoadedEventHandler();
    [Signal] public delegate void DataValidationFailedEventHandler(string message);

    // --- Emit helpers -------------------------------------------------------

    public void EmitGameStarted() => EmitSignal(SignalName.GameStarted);
    public void EmitGameLoaded() => EmitSignal(SignalName.GameLoaded);
    public void EmitGameSaved() => EmitSignal(SignalName.GameSaved);

    public void EmitTurnStarted(int turn) => EmitSignal(SignalName.TurnStarted, turn);
    public void EmitTurnEnded(int turn) => EmitSignal(SignalName.TurnEnded, turn);

    public void EmitProvinceSelected(int provinceId) => EmitSignal(SignalName.ProvinceSelected, provinceId);
    public void EmitCountrySelected(int countryId) => EmitSignal(SignalName.CountrySelected, countryId);

    public void EmitEconomyUpdated() => EmitSignal(SignalName.EconomyUpdated);
    public void EmitTradeUpdated() => EmitSignal(SignalName.TradeUpdated);
    public void EmitDiplomacyUpdated() => EmitSignal(SignalName.DiplomacyUpdated);
    public void EmitWarDeclared(int attackerId, int defenderId) => EmitSignal(SignalName.WarDeclared, attackerId, defenderId);
    public void EmitPeaceSigned(int countryAId, int countryBId) => EmitSignal(SignalName.PeaceSigned, countryAId, countryBId);
    public void EmitPopulationUpdated() => EmitSignal(SignalName.PopulationUpdated);

    public void EmitUINotification(string text) => EmitSignal(SignalName.UINotification, text);
    public void EmitSettingsChanged() => EmitSignal(SignalName.SettingsChanged);
    public void EmitLocalizationChanged() => EmitSignal(SignalName.LocalizationChanged);
    public void EmitErrorOccurred(string message) => EmitSignal(SignalName.ErrorOccurred, message);
    public void EmitDataLoaded() => EmitSignal(SignalName.DataLoaded);
    public void EmitDataValidationFailed(string message) => EmitSignal(SignalName.DataValidationFailed, message);
}
