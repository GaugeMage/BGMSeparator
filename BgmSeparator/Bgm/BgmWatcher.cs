using System;

namespace BgmSeparator.Bgm;

/// <summary>
/// Polls the game's BGM scene list every framework tick and raises an event when the
/// highest-priority (audible) song changes. This is the read-only detection half of
/// what Orchestrion does; we never write to the scene list.
/// </summary>
public sealed class BgmWatcher : IDisposable
{
    private const int SceneCount = 12;

    /// <summary>The BGM row id the game is currently playing at the highest scene (0 = none).</summary>
    public int CurrentSongId { get; private set; }

    /// <summary>The scene index (0-11) the current song was found at (-1 = none). Higher-priority = lower index.</summary>
    public int CurrentSceneIndex { get; private set; } = -1;

    /// <summary>Fires with (oldSongId, newSongId) whenever the audible song changes.</summary>
    public event Action<int, int>? SongChanged;

    public BgmWatcher()
    {
        BgmAddressResolver.Init();
        Services.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Services.Framework.Update -= OnFrameworkUpdate;
    }

    private unsafe void OnFrameworkUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        var listPtr = BgmAddressResolver.BgmSceneList;
        if (listPtr == nint.Zero) return;

        var scenes = (BgmScene*)listPtr;
        ushort current = 0;
        var currentScene = -1;

        for (var i = 0; i < SceneCount; i++)
        {
            if (scenes[i].BgmReference == 0) continue;
            var id = scenes[i].BgmId;
            if (id != 0 && id != 9999)
            {
                current = id;
                currentScene = i;
                break; // highest-priority non-empty scene wins
            }
        }

        if (current != CurrentSongId)
        {
            var old = CurrentSongId;
            CurrentSongId = current;
            CurrentSceneIndex = currentScene;
            try
            {
                SongChanged?.Invoke(old, current);
            }
            catch (Exception ex)
            {
                Services.Log.Error(ex, "[BgmSeparator] SongChanged handler threw");
            }
        }
    }
}
