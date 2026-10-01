using System;
using System.Threading.Tasks;
using BgmSeparator.Bgm;
using Dalamud.Game.Config;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace BgmSeparator.Audio;

/// <summary>
/// Glue between BGM detection and audio output: when the game's audible song changes,
/// load the matching music file and crossfade it in on the separate output device.
/// Also manages muting the game's own BGM channel so it stays out of the main capture.
/// </summary>
public sealed class BgmPlaybackCoordinator : IDisposable
{
    // FFXIV "Mute BGM" checkbox in Sound settings. Name matches SystemConfigOption.IsSndBgm.
    private const string BgmMuteKey = "IsSndBgm";

    // Fade fallbacks (ms) used only when the game doesn't supply its own fade timing.
    // Combat stays tight; field transitions get a gentle fade. Not user-facing by design.
    private const int CombatFadeMs = 400;
    private const int FieldFadeMs = 2000;

    private sealed class Track : IDisposable
    {
        public required VorbisLoopSampleProvider Source;
        public required FadeSampleProvider Fade;
        public int SceneIndex = -1;
        public void Dispose() => Source.Dispose();
    }

    private readonly Configuration _config;
    private readonly AudioEngine _engine;
    private readonly BgmWatcher _watcher;

    private readonly object _lock = new();
    private Track? _current;
    private int _loadToken;
    private int _currentSongId;

    /// <summary>The BGM id the plugin is currently outputting (0 = none). Used by diagnostics.</summary>
    public int CurrentTrackId => _currentSongId;

    /// <summary>The BGM id the game itself reports as audible right now (matches Orchestrion). 0 = silence.</summary>
    public int GameSongId => _watcher.CurrentSongId;

    /// <summary>The scene index (0-11) the game's audible song is playing at. -1 = none.</summary>
    public int GameSceneIndex => _watcher.CurrentSceneIndex;

    /// <summary>True while the game has battle BGM active.</summary>
    public bool IsBattle => GameAudioState.IsBattleBgm();

    private bool _bgmMuted;
    private bool _savedBgmMute;
    private bool _hadSavedBgmMute;

    public BgmPlaybackCoordinator(Configuration config)
    {
        _config = config;
        _engine = new AudioEngine();
        _watcher = new BgmWatcher();
        _watcher.SongChanged += OnSongChanged;
    }

    public static System.Collections.Generic.List<AudioDeviceInfo> ListDevices() => AudioEngine.ListRenderDevices();

    public void Start()
    {
        _engine.EnsureDevice(_config.OutputDeviceId);
        if (_config.MuteInGameBgm) MuteGameBgm(true);

        // Kick off the currently-playing song immediately.
        var now = _watcher.CurrentSongId;
        if (now != 0) OnSongChanged(0, now);
    }

    public void Stop()
    {
        lock (_lock)
        {
            _loadToken++;
            _engine.ClearInputs();
            _current?.Dispose();
            _current = null;
            _currentSongId = 0;
        }
        MuteGameBgm(false);
    }

    public void ApplyDeviceChange() => _engine.Start(_config.OutputDeviceId);

    public void ApplyVolumeChange()
    {
        lock (_lock)
        {
            if (_current != null) _current.Fade.Volume = _config.Volume;
        }
    }

    public void ApplyMuteSettingChange() => MuteGameBgm(_config.MuteInGameBgm);

    /// <summary>True while the separated output is enabled.</summary>
    public bool IsEnabled => _config.Enabled;

    /// <summary>
    /// Turns the separated output on or off, persisting the choice. This is the single
    /// path used by both the config window and the /bgmsep command so they can't drift.
    /// Returns the state actually in effect afterwards.
    /// </summary>
    public bool SetEnabled(bool enabled)
    {
        if (_config.Enabled == enabled) return enabled;

        _config.Enabled = enabled;
        _config.Save();

        try
        {
            if (enabled) Start();
            else Stop();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, $"[BgmSeparator] Failed to turn separated output {(enabled ? "on" : "off")}");
        }

        return _config.Enabled;
    }

    /// <summary>Flips the separated output. Returns the new state.</summary>
    public bool ToggleEnabled() => SetEnabled(!_config.Enabled);

    private void OnSongChanged(int oldSong, int newSong)
    {
        if (!_config.Enabled) return;

        var token = ++_loadToken;
        var newScene = _watcher.CurrentSceneIndex;

        if (newSong == 0)
        {
            // Game went silent: fade out using the stopping scene's own timing so we cut
            // as promptly as the game does (short in combat, long for zone changes).
            FadeOutCurrent();
            return;
        }

        var fadeInMs = ResolveFadeIn(newScene);

        // Decode off the framework thread to avoid frame hitches.
        Task.Run(() =>
        {
            var ogg = ScdBgmLoader.LoadOgg(newSong);
            if (ogg == null)
            {
                // The game switched to a BGM with no playable file (e.g. a cutscene or
                // placeholder like row 1). Match the game and go silent instead of looping
                // the previous track forever.
                if (token == _loadToken) FadeOutCurrent();
                return;
            }

            try
            {
                var src = new VorbisLoopSampleProvider(ogg, _config.LoopUntaggedTracks);
                ISampleProvider chain = src;
                if (chain.WaveFormat.Channels == 1)
                    chain = new MonoToStereoSampleProvider(chain);
                if (chain.WaveFormat.SampleRate != AudioEngine.MixFormat.SampleRate)
                    chain = new WdlResamplingSampleProvider(chain, AudioEngine.MixFormat.SampleRate);

                var fade = new FadeSampleProvider(chain, startSilent: true) { Volume = _config.Volume };
                fade.BeginFadeIn(fadeInMs);

                lock (_lock)
                {
                    if (token != _loadToken)
                    {
                        src.Dispose();
                        return; // superseded by a newer change
                    }

                    FadeOutCurrent();

                    _current = new Track { Source = src, Fade = fade, SceneIndex = newScene };
                    _currentSongId = newSong;
                    _engine.AddInput(fade);
                }
            }
            catch (Exception ex)
            {
                Services.Log.Error(ex, $"[BgmSeparator] Failed to start BGM {newSong}");
                if (token == _loadToken) FadeOutCurrent();
            }
        });
    }

    private void FadeOutCurrent()
    {
        Track? old;
        lock (_lock)
        {
            old = _current;
            _current = null;
            _currentSongId = 0;
        }
        if (old == null) return;

        var fadeMs = Math.Max(1, ResolveFadeOut(old.SceneIndex));
        old.Fade.BeginFadeOut(fadeMs);
        Task.Delay(fadeMs + 250).ContinueWith(_ =>
        {
            _engine.RemoveInput(old.Fade);
            old.Dispose();
        });
    }

    // Fade duration precedence:
    //   1) the game's own custom fade time for that scene (exact match to the game), else
    //   2) a short "combat" fade while battle BGM is active so rapid phase flips stay tight, else
    //   3) the user's slider (cinematic zone/teleport fades).
    private static int ResolveFadeIn(int sceneIndex)
    {
        if (GameAudioState.TryGetSceneFade(sceneIndex, out var fi, out _) && fi > 0)
            return fi;
        return GameAudioState.IsBattleBgm() ? CombatFadeMs : FieldFadeMs;
    }

    private static int ResolveFadeOut(int sceneIndex)
    {
        if (GameAudioState.TryGetSceneFade(sceneIndex, out _, out var fo) && fo > 0)
            return fo;
        return GameAudioState.IsBattleBgm() ? CombatFadeMs : FieldFadeMs;
    }

    private void MuteGameBgm(bool mute)
    {
        try
        {
            if (mute)
            {
                if (_bgmMuted) return;
                if (Services.GameConfig.System.TryGetBool(BgmMuteKey, out var cur))
                {
                    _savedBgmMute = cur;
                    _hadSavedBgmMute = true;
                }
                Services.GameConfig.System.Set(BgmMuteKey, true);
                _bgmMuted = true;
            }
            else
            {
                if (!_bgmMuted) return;
                // Restore whatever the user had before (default: unmuted).
                var restore = _hadSavedBgmMute && _savedBgmMute;
                Services.GameConfig.System.Set(BgmMuteKey, restore);
                _bgmMuted = false;
            }
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "[BgmSeparator] Failed to toggle in-game BGM mute");
        }
    }

    public void Dispose()
    {
        _watcher.SongChanged -= OnSongChanged;
        Stop();
        _watcher.Dispose();
        _engine.Dispose();
    }
}
