namespace GrandStrategy.Core;

/// <summary>
/// Центральные константы проекта и fallback-значения баланса.
/// Правило: реальный баланс живёт в data/*.json / data/*.csv; здесь — только
/// значения по умолчанию, к которым обращается код при отсутствии данных.
/// </summary>
public static class GameConstants
{
    // Version
    public const string AppVersion = "0.1.0";

    // Localization
    public const string DefaultLanguage = "en";
    public const string GameNameKey = "APP_NAME";

    // Time
    public const int DefaultStartYear = 2024;
    public const int DaysPerTurn = 1; // одна единица хода = 1 игровой день (настраивается позже)

    // Save
    public const string AutosaveSlot = "autosave";
    public const string SaveFileExtension = ".sav.json";

    // Logging
    public const string LogDirectory = "user://logs";
}
