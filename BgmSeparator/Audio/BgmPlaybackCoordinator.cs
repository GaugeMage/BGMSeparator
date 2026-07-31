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

    private sealed class Track : IDisposable
    {
        public required VorbisLoopSampleProvider Source;
        public required FadeInOutSampleProvider Fade;
        public required VolumeSampleProvider Volume;
        public void Dispose() => Source.Dispose();
    }

    private readonly Configuration _config;
    private readonly AudioEngine _engine;
    private readonly BgmWatcher _watcher;

    private readonly object _lock = new();
    private Track? _current;
    private int _loadToken;

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
        }
        MuteGameBgm(false);
    }

    public void ApplyDeviceChange() => _engine.Start(_config.OutputDeviceId);

    public void ApplyVolumeChange()
    {
        lock (_lock)
        {
            if (_current != null) _current.Volume.Volume = _config.Volume;
        }
    }

    public void ApplyMuteSettingChange() => MuteGameBgm(_config.MuteInGameBgm);

    private void OnSongChanged(int oldSong, int newSong)
    {
        if (!_config.Enabled) return;

        var token = ++_loadToken;

        if (newSong == 0)
        {
            FadeOutCurrent();
            return;
        }

        // Decode off the framework thread to avoid frame hitches.
        Task.Run(() =>
        {
            var ogg = ScdBgmLoader.LoadOgg(newSong);
            if (ogg == null) return;

            try
            {
                var src = new VorbisLoopSampleProvider(ogg, _config.LoopUntaggedTracks);
                ISampleProvider chain = src;
                if (chain.WaveFormat.Channels == 1)
                    chain = new MonoToStereoSampleProvider(chain);
                if (chain.WaveFormat.SampleRate != AudioEngine.MixFormat.SampleRate)
                    chain = new WdlResamplingSampleProvider(chain, AudioEngine.MixFormat.SampleRate);

                var fade = new FadeInOutSampleProvider(chain, initiallySilent: true);
                fade.BeginFadeIn(Math.Max(1, _config.CrossfadeMs));
                var vol = new VolumeSampleProvider(fade) { Volume = _config.Volume };

                lock (_lock)
                {
                    if (token != _loadToken)
                    {
                        src.Dispose();
                        return; // superseded by a newer change
                    }

                    FadeOutCurrent();

                    _current = new Track { Source = src, Fade = fade, Volume = vol };
                    _engine.AddInput(vol);
                }
            }
            catch (Exception ex)
            {
                Services.Log.Error(ex, $"[BgmSeparator] Failed to start BGM {newSong}");
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
        }
        if (old == null) return;

        var fadeMs = Math.Max(1, _config.CrossfadeMs);
        old.Fade.BeginFadeOut(fadeMs);
        Task.Delay(fadeMs + 100).ContinueWith(_ =>
        {
            _engine.RemoveInput(old.Volume);
            old.Dispose();
        });
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
