using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;

namespace GrandStrategy.Core;

/// <summary>
/// Снимок сохранения (верхнеуровневые ключи, см. формат M9).
/// world_state — заглушка: в M9 станет конкретным графом объектов
/// (provinces, countries, economy, trade, diplomacy, wars, events, ai_state).
/// </summary>
public sealed class GameSnapshot
{
    public int SaveVersion { get; init; } = SaveManager.SaveVersion;
    public string GameVersion { get; init; } = GameConstants.AppVersion;
    public string Timestamp { get; init; } = string.Empty;
    public string CurrentDate { get; init; } = string.Empty;
    public int CurrentTurn { get; init; }
    public int PlayerCountryId { get; init; }
    public long Seed { get; init; }
    public string Difficulty { get; init; } = "normal";
    public bool Ironman { get; init; }
    public Settings? Settings { get; init; }
    public Dictionary<string, object?>? WorldState { get; init; }
}

/// <summary>
/// Контейнер файла сохранения: payload + контрольная сумма (SHA-256) для защиты от
/// повреждения. Сумма считается по сериализованному payload и сверяется при загрузке.
/// </summary>
internal sealed class SaveContainer
{
    public string Checksum { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
}

/// <summary>
/// Синглтон сохранений. JSON (System.Text.Json), версионирование, контрольная сумма,
/// атомарная запись (temp+rename), автосейв, graceful-обработка повреждённых файлов.
/// Autoload #8.
/// </summary>
public partial class SaveManager : Node
{
    public static SaveManager Instance { get; private set; } = null!;

    public const int SaveVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public override void _Ready()
    {
        Instance = this;
        Directory.CreateDirectory(GetSaveDirectory());
    }

    private static string GetSaveDirectory() =>
        ProjectSettings.GlobalizePath("user://saves");

    private static string SlotPath(string slotName) =>
        Path.Combine(GetSaveDirectory(), slotName + GameConstants.SaveFileExtension);

    public bool SaveExists(string slotName) => File.Exists(SlotPath(slotName));

    public string[] ListSaves()
    {
        string dir = GetSaveDirectory();
        if (!Directory.Exists(dir))
            return Array.Empty<string>();

        var result = new List<string>();
        foreach (string file in Directory.GetFiles(dir, "*" + GameConstants.SaveFileExtension))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            name = Path.GetFileNameWithoutExtension(name); // убираем ".sav"
            result.Add(name);
        }
        return result.ToArray();
    }

    public void DeleteSave(string slotName)
    {
        string path = SlotPath(slotName);
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>Сохраняет снимок. Атомарно: temp-файл + rename, с контрольной суммой.</summary>
    public bool SaveGame(string slotName, GameSnapshot snapshot)
    {
        try
        {
            string payload = JsonSerializer.Serialize(snapshot, JsonOptions);
            string checksum = ComputeSha256(payload);
            var container = new SaveContainer { Checksum = checksum, PayloadJson = payload };

            string json = JsonSerializer.Serialize(container, JsonOptions);
            string tmp = SlotPath(slotName) + ".tmp";
            string final = SlotPath(slotName);

            File.WriteAllText(tmp, json, Encoding.UTF8);
            if (File.Exists(final))
                File.Delete(final);
            File.Move(tmp, final);

            LogService.Instance.Info($"Save: '{slotName}' written ({json.Length} bytes)");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"Save '{slotName}' failed: {ex.Message}");
            EventBus.Instance.EmitErrorOccurred(ex.Message);
            return false;
        }
    }

    /// <summary>Загружает снимок; возвращает null при повреждении/ошибке.</summary>
    public GameSnapshot? LoadGame(string slotName)
    {
        string path = SlotPath(slotName);
        if (!File.Exists(path))
        {
            LogService.Instance.Warning($"Save: '{slotName}' not found");
            return null;
        }

        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            SaveContainer? container = JsonSerializer.Deserialize<SaveContainer>(json, JsonOptions);
            if (container == null)
                throw new InvalidDataException("empty container");

            string actual = ComputeSha256(container.PayloadJson);
            if (!string.Equals(actual, container.Checksum, StringComparison.Ordinal))
                throw new InvalidDataException("checksum mismatch (corrupted file)");

            GameSnapshot? snapshot = JsonSerializer.Deserialize<GameSnapshot>(container.PayloadJson, JsonOptions);
            if (snapshot == null)
                throw new InvalidDataException("empty payload");

            if (snapshot.SaveVersion > SaveVersion)
                LogService.Instance.Warning($"Save: version {snapshot.SaveVersion} newer than {SaveVersion}, migration may be needed");

            LogService.Instance.Info($"Save: '{slotName}' loaded (turn {snapshot.CurrentTurn})");
            return snapshot;
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"Load '{slotName}' failed: {ex.Message}");
            EventBus.Instance.EmitErrorOccurred($"Load failed: {ex.Message}");
            return null;
        }
    }

    public void AutoSave(GameSnapshot snapshot) =>
        SaveGame(GameConstants.AutosaveSlot, snapshot);

    private static string ComputeSha256(string input)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
