using System;
using Dalamud.Configuration;

namespace BgmSeparator;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Master enable for the separate BGM output.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The WASAPI render device ID (MMDevice.ID) to play the separated BGM to.
    /// Empty => use the system default render device.
    /// </summary>
    public string OutputDeviceId { get; set; } = string.Empty;

    /// <summary>Human-readable name of the selected device, for UI display only.</summary>
    public string OutputDeviceName { get; set; } = string.Empty;

    /// <summary>Playback volume for the separated BGM (0.0 - 1.0).</summary>
    public float Volume { get; set; } = 1.0f;

    /// <summary>
    /// When true, the plugin sets the in-game BGM volume (SoundBgm) to 0 while active,
    /// so the game's own mixed output (captured by OBS Desktop Audio) contains no music.
    /// The original value is restored on unload / disable.
    /// </summary>
    public bool MuteInGameBgm { get; set; } = true;

    /// <summary>Crossfade length between BGM changes, in milliseconds.</summary>
    public int CrossfadeMs { get; set; } = 400;

    /// <summary>
    /// If a track has no LOOPSTART/LOOPEND tags, loop the whole file when true,
    /// or play once (like a fanfare) when false.
    /// </summary>
    public bool LoopUntaggedTracks { get; set; } = true;

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}
