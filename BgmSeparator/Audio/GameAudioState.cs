using System;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Sound;

namespace BgmSeparator.Audio;

/// <summary>A read-only snapshot of one of the game's 12 BGM scenes.</summary>
public struct SceneSnapshot
{
    public int SceneId;
    public int BgmId;
    public int PlayingBgmId;
    public uint Situation;
    public uint PlayState;
    public uint FadeOutMs;
    public uint FadeInMs;
    public uint FadeInStartMs;
    public bool CustomFade;
    public float InitialVolume;
    public int IntermissionBgmId;
    public float IntermissionElapsed;
    public float IntermissionResetWait;
    public bool IntermissionEnabled;
}

/// <summary>
/// Reads live audio state from the game via FFXIVClientStructs: the real-time effective
/// music volume (the game's own fade/duck envelope) and per-scene fade timings. This is
/// what lets us mirror the game instead of guessing at fades.
/// </summary>
public static class GameAudioState
{
    /// <summary>The game's current effective BGM bus volume (0-1), or -1 if unavailable.</summary>
    public static unsafe float MusicEffectiveVolume()
    {
        try
        {
            var sm = SoundManager.Instance();
            if (sm == null) return -1f;
            return sm->GetEffectiveVolume(SoundBus.Music);
        }
        catch
        {
            return -1f;
        }
    }

    /// <summary>Fills <paramref name="buffer"/> with the game's BGM scenes; returns the count written.</summary>
    public static unsafe int SnapshotScenes(Span<SceneSnapshot> buffer)
    {
        var bgm = BGMSystem.Instance();
        if (bgm == null) return 0;

        var first = bgm->Scenes.First;
        var last = bgm->Scenes.Last;
        if (first == null || last == null) return 0;

        var count = (int)(last - first);
        var n = 0;
        for (var i = 0; i < count && n < buffer.Length; i++)
        {
            ref var s = ref first[i];

            var snap = new SceneSnapshot
            {
                SceneId = (int)s.SceneId,
                BgmId = s.BgmId,
                PlayingBgmId = s.PlayingBgmId,
                Situation = (uint)s.SituationKind,
                PlayState = (uint)s.PlayState,
                FadeOutMs = s.FadeOutTime,
                FadeInMs = s.FadeInTime,
                FadeInStartMs = s.FadeInStartTime,
                CustomFade = s.EnableCustomFade,
                InitialVolume = s.InitialVolume,
            };

            var inter = s.Intermissions.First;
            if (inter != null && s.Intermissions.Last != inter)
            {
                snap.IntermissionBgmId = inter->BgmId;
                snap.IntermissionElapsed = inter->ElapsedTime;
                snap.IntermissionResetWait = inter->ResetWaitTime;
                snap.IntermissionEnabled = inter->IsEnabled;
            }

            buffer[n++] = snap;
        }

        return n;
    }

    /// <summary>True while the game has battle BGM active (rapid phase transitions expected).</summary>
    public static unsafe bool IsBattleBgm()
    {
        var bgm = BGMSystem.Instance();
        if (bgm == null) return false;
        return bgm->PlayBattleBGM || bgm->CurrentSituationKind == BGMSystem.SituationKind.Battle;
    }

    /// <summary>
    /// Reads the game's own fade-in/out times for a specific scene index, but only when
    /// that scene opts into a custom fade. Returns false otherwise (use a fallback).
    /// </summary>
    public static unsafe bool TryGetSceneFade(int sceneIndex, out int fadeInMs, out int fadeOutMs)
    {
        fadeInMs = 0;
        fadeOutMs = 0;
        if (sceneIndex < 0) return false;

        var bgm = BGMSystem.Instance();
        if (bgm == null) return false;

        var first = bgm->Scenes.First;
        var last = bgm->Scenes.Last;
        if (first == null || last == null) return false;

        var count = (int)(last - first);
        if (sceneIndex >= count) return false;

        ref var s = ref first[sceneIndex];
        if (!s.EnableCustomFade) return false;

        fadeInMs = (int)s.FadeInTime;
        fadeOutMs = (int)s.FadeOutTime;
        return fadeInMs > 0 || fadeOutMs > 0;
    }

    /// <summary>
    /// Finds the game's configured fade times for a given BGM id, if that scene uses a
    /// custom fade. Returns false when the game isn't providing explicit fade timings.
    /// </summary>
    public static unsafe bool TryGetFadeForBgm(int bgmId, out int fadeInMs, out int fadeOutMs)
    {
        fadeInMs = 0;
        fadeOutMs = 0;
        if (bgmId <= 0) return false;

        var bgm = BGMSystem.Instance();
        if (bgm == null) return false;

        var first = bgm->Scenes.First;
        var last = bgm->Scenes.Last;
        if (first == null || last == null) return false;

        var count = (int)(last - first);
        for (var i = 0; i < count; i++)
        {
            ref var s = ref first[i];
            if ((s.PlayingBgmId == bgmId || s.BgmId == bgmId) && s.EnableCustomFade)
            {
                fadeInMs = (int)s.FadeInTime;
                fadeOutMs = (int)s.FadeOutTime;
                return fadeInMs > 0 || fadeOutMs > 0;
            }
        }

        return false;
    }
}
