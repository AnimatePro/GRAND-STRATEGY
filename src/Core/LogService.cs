using System;
using System.IO;
using Godot;

namespace GrandStrategy.Core;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>
/// Синглтон логирования. Пишет в user://logs/game_YYYY-MM-DD.log (потокобезопасно)
/// и дублирует сообщения в консоль Godot (GD.Print / PushWarning / PushError).
/// Подписан на EventBus.ErrorOccurred / DataValidationFailed как центральный сток ошибок.
/// Autoload #2.
/// </summary>
public partial class LogService : Node
{
    public static LogService Instance { get; private set; } = null!;

    private readonly object _lock = new();
    private string _logFilePath = string.Empty;

    public override void _Ready()
    {
        Instance = this;
        _logFilePath = BuildLogFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);

        // Центральный сток ошибок: ошибки из EventBus попадают в лог.
        EventBus.Instance.ErrorOccurred += OnErrorOccurred;
        EventBus.Instance.DataValidationFailed += OnValidationFailed;

        Info($"{GameConstants.AppVersion} | LogService initialized | file={_logFilePath}");
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.ErrorOccurred -= OnErrorOccurred;
            EventBus.Instance.DataValidationFailed -= OnValidationFailed;
        }
    }

    public void Debug(string message) => Write(LogLevel.Debug, message);
    public void Info(string message) => Write(LogLevel.Info, message);
    public void Warning(string message) => Write(LogLevel.Warning, message);
    public void Error(string message) => Write(LogLevel.Error, message);

    private static string BuildLogFilePath()
    {
        string dir = ProjectSettings.GlobalizePath(GameConstants.LogDirectory);
        string stamp = DateTime.Now.ToString("yyyy-MM-dd");
        return Path.Combine(dir, $"game_{stamp}.log");
    }

    private void Write(LogLevel level, string message)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level.ToString().ToUpperInvariant()}] {message}";

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logFilePath, line + System.Environment.NewLine);
            }
            catch
            {
                // Ошибки ввода-вывода лога не должны ронять игру.
            }
        }

        switch (level)
        {
            case LogLevel.Error:
                GD.PushError(message);
                break;
            case LogLevel.Warning:
                GD.PushWarning(message);
                break;
            case LogLevel.Debug:
                if (OS.IsDebugBuild())
                    GD.Print(message);
                break;
            default:
                GD.Print(message);
                break;
        }
    }

    private void OnErrorOccurred(string message) => Error(message);
    private void OnValidationFailed(string message) => Error($"Data validation failed: {message}");
}
