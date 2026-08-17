using System.Collections.Generic;
using Godot;
using GrandStrategy.Core;

namespace GrandStrategy.UI;

/// <summary>
/// Менеджер звука (Autoload #16). Простые короткие клики/сигналы (без музыки).
/// Загружает WAV из assets/audio, играет через пул AudioStreamPlayer.
/// Громкость берётся из настроек (AudioSettings.UiVolume / SfxVolume).
/// </summary>
public partial class AudioManager : Node
{
    public static AudioManager Instance { get; private set; } = null!;

    public enum Sfx
    {
        Click, Hover, Select, Notify, War, Peace, Error, EndTurn,
    }

    private readonly Dictionary<Sfx, AudioStream> _streams = new();
    private readonly List<AudioStreamPlayer> _pool = new();
    private int _next = 0;

    public override void _Ready()
    {
        Instance = this;
        LoadStreams();
        for (int i = 0; i < 8; i++)
        {
            var player = new AudioStreamPlayer();
            AddChild(player);
            _pool.Add(player);
        }

        // Автоматическая озвучка системных событий.
        EventBus.Instance.ProvinceSelected += _ => Play(Sfx.Select);
        EventBus.Instance.CountrySelected += _ => Play(Sfx.Select);
        EventBus.Instance.UINotification += _ => Play(Sfx.Notify);
        EventBus.Instance.WarDeclared += (_, __) => Play(Sfx.War);
        EventBus.Instance.PeaceSigned += (_, __) => Play(Sfx.Peace);
        EventBus.Instance.ErrorOccurred += _ => Play(Sfx.Error);
        EventBus.Instance.TurnEnded += _ => Play(Sfx.EndTurn);
    }

    private void LoadStreams()
    {
        Load(Sfx.Click, "res://assets/audio/click.wav");
        Load(Sfx.Hover, "res://assets/audio/hover.wav");
        Load(Sfx.Select, "res://assets/audio/select.wav");
        Load(Sfx.Notify, "res://assets/audio/notify.wav");
        Load(Sfx.War, "res://assets/audio/war.wav");
        Load(Sfx.Peace, "res://assets/audio/peace.wav");
        Load(Sfx.Error, "res://assets/audio/error.wav");
        Load(Sfx.EndTurn, "res://assets/audio/endturn.wav");
    }

    private void Load(Sfx id, string path)
    {
        if (ResourceLoader.Exists(path))
        {
            _streams[id] = GD.Load<AudioStream>(path);
        }
        else
        {
            LogService.Instance.Warning($"AudioManager: missing {path}");
        }
    }

    /// <summary>Проиграть клик (используется кнопками UI).</summary>
    public void Play(Sfx sfx)
    {
        if (!_streams.TryGetValue(sfx, out AudioStream? stream))
            return;

        float volume = sfx switch
        {
            Sfx.Click or Sfx.Hover or Sfx.Select or Sfx.Notify or Sfx.EndTurn
                => SettingsManager.Instance.Current.Audio.UiVolume,
            _ => SettingsManager.Instance.Current.Audio.SfxVolume,
        };
        volume *= SettingsManager.Instance.Current.Audio.MasterVolume;
        if (volume <= 0.001f)
            return;

        AudioStreamPlayer player = _pool[_next];
        _next = (_next + 1) % _pool.Count;
        player.Stream = stream;
        player.VolumeDb = Mathf.LinearToDb(volume);
        player.Play();
    }

    public void PlayClick() => Play(Sfx.Click);
    public void PlayHover() => Play(Sfx.Hover);
}
