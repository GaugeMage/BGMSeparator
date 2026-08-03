using System;
using System.IO;
using System.Text;
using BgmSeparator.Audio;

namespace BgmSeparator.Diagnostics;

/// <summary>
/// Records a CSV timeline of the game's full BGM state (all scenes, situations, fade times,
/// intermission timers) plus the game's real-time music volume and what the plugin is
/// playing. Run this during an Ultimate pull to capture exactly how the game drives its
/// dynamic/phase music, so we can reproduce it precisely.
/// </summary>
public sealed class BgmDiagnostics : IDisposable
{
    private const int IntervalMs = 100; // 10 Hz

    private readonly object _lock = new();
    private StreamWriter? _writer;
    private DateTime _start;
    private long _lastWriteMs;
    private Func<int>? _pluginSong;

    public bool IsRecording { get; private set; }
    public string? CurrentPath { get; private set; }

    public string Start(Func<int> pluginSong)
    {
        lock (_lock)
        {
            StopInternal();

            var dir = Services.PluginInterface.GetPluginConfigDirectory();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"bgm-diag-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

            _writer = new StreamWriter(path, false, Encoding.UTF8) { AutoFlush = true };
            _writer.WriteLine("t_ms,pluginSong,musicVol,curSituation,playBattle,sceneId,bgmId,playingBgmId,sceneSituation,playState,fadeOutMs,fadeInMs,fadeInStartMs,customFade,initialVol,interBgmId,interElapsed,interResetWait,interEnabled");

            _pluginSong = pluginSong;
            _start = DateTime.UtcNow;
            _lastWriteMs = -IntervalMs * 2;
            IsRecording = true;
            CurrentPath = path;
            return path;
        }
    }

    public unsafe void Tick()
    {
        if (!IsRecording) return;

        lock (_lock)
        {
            if (_writer == null) return;

            var ms = (long)(DateTime.UtcNow - _start).TotalMilliseconds;
            if (ms - _lastWriteMs < IntervalMs) return;
            _lastWriteMs = ms;

            uint curSit = 0;
            var playBattle = false;
            var bgm = FFXIVClientStructs.FFXIV.Client.Game.BGMSystem.Instance();
            if (bgm != null)
            {
                curSit = (uint)bgm->CurrentSituationKind;
                playBattle = bgm->PlayBattleBGM;
            }

            var vol = GameAudioState.MusicEffectiveVolume();
            var pluginSong = _pluginSong?.Invoke() ?? 0;

            Span<SceneSnapshot> scenes = stackalloc SceneSnapshot[24];
            var n = GameAudioState.SnapshotScenes(scenes);

            var wroteRow = false;
            for (var i = 0; i < n; i++)
            {
                var s = scenes[i];
                if (s.BgmId == 0 && s.PlayingBgmId == 0) continue;
                wroteRow = true;
                _writer.WriteLine(
                    $"{ms},{pluginSong},{vol:F3},{curSit},{(playBattle ? 1 : 0)}," +
                    $"{s.SceneId},{s.BgmId},{s.PlayingBgmId},{s.Situation},{s.PlayState}," +
                    $"{s.FadeOutMs},{s.FadeInMs},{s.FadeInStartMs},{(s.CustomFade ? 1 : 0)},{s.InitialVolume:F3}," +
                    $"{s.IntermissionBgmId},{s.IntermissionElapsed:F2},{s.IntermissionResetWait:F2},{(s.IntermissionEnabled ? 1 : 0)}");
            }

            if (!wroteRow)
                _writer.WriteLine($"{ms},{pluginSong},{vol:F3},{curSit},{(playBattle ? 1 : 0)},,,,,,,,,,,,,,");
        }
    }

    public void Stop()
    {
        lock (_lock) StopInternal();
    }

    private void StopInternal()
    {
        if (_writer != null)
        {
            try { _writer.Flush(); _writer.Dispose(); } catch { }
            _writer = null;
        }
        IsRecording = false;
    }

    public void Dispose() => Stop();
}
