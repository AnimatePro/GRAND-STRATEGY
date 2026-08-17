using GrandStrategy.Data;

namespace GrandStrategy.Core;

/// <summary>
/// Прозрачные модификаторы сложности (никаких скрытых читов ИИ).
/// Сложность хранится в NewGameOptions.Difficulty как строка-число ("0".."4").
/// Бонусы применяются симметрично: на низкой сложности игрок усиливается, ИИ ослабляется.
/// </summary>
public static class DifficultyModifiers
{
    public static int Level
    {
        get
        {
            string? s = GameManager.Instance.ActiveOptions?.Difficulty;
            return int.TryParse(s, out int l) ? l : (int)Difficulty.Normal;
        }
    }

    /// <summary>Множитель экономики (производства) для страны.</summary>
    public static double EconomyMult(int countryId)
    {
        bool isPlayer = IsPlayer(countryId);
        return Level switch
        {
            (int)Difficulty.VeryEasy => isPlayer ? 1.25 : 0.75,
            (int)Difficulty.Easy => isPlayer ? 1.10 : 0.90,
            (int)Difficulty.Hard => isPlayer ? 1.00 : 1.15,
            (int)Difficulty.VeryHard => isPlayer ? 1.00 : 1.35,
            _ => 1.00,
        };
    }

    /// <summary>Множитель военной силы (атака/оборона) для страны.</summary>
    public static double MilitaryMult(int countryId)
    {
        bool isPlayer = IsPlayer(countryId);
        return Level switch
        {
            (int)Difficulty.VeryEasy => isPlayer ? 1.20 : 0.80,
            (int)Difficulty.Easy => isPlayer ? 1.10 : 0.90,
            (int)Difficulty.Hard => isPlayer ? 1.00 : 1.15,
            (int)Difficulty.VeryHard => isPlayer ? 1.00 : 1.30,
            _ => 1.00,
        };
    }

    private static bool IsPlayer(int countryId)
    {
        if (!DataManager.Instance.IsLoaded || countryId < 0 || countryId >= DataManager.Instance.World.Countries.Length)
            return false;
        return DataManager.Instance.World.Countries[countryId]?.IsPlayer ?? false;
    }
}
