using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using BgmSeparator.Audio;
using BgmSeparator.Diagnostics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace BgmSeparator.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Configuration _config;
    private readonly BgmPlaybackCoordinator _coordinator;
    private readonly BgmDiagnostics _diagnostics;

    private List<AudioDeviceInfo> _devices = new();

    public ConfigWindow(Configuration config, BgmPlaybackCoordinator coordinator, BgmDiagnostics diagnostics)
        : base("BGM Separator###BgmSeparatorConfig")
    {
        _config = config;
        _coordinator = coordinator;
        _diagnostics = diagnostics;
        Size = new Vector2(460, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
        RefreshDevices();
    }

    private void RefreshDevices() => _devices = BgmPlaybackCoordinator.ListDevices();

    public override void Draw()
    {
        var enabled = _config.Enabled;
        if (ImGui.Checkbox("Enable separated BGM output", ref enabled))
            _coordinator.SetEnabled(enabled);
        ImGui.TextDisabled("Keybind it: make a macro with \"/bgmsep toggle\" and drag it to a hotbar.");

        ImGui.Separator();
        ImGui.TextUnformatted("Output device");
        ImGui.SameLine();
        if (ImGui.SmallButton("Refresh")) RefreshDevices();

        var currentName = string.IsNullOrEmpty(_config.OutputDeviceId)
            ? "System default"
            : string.IsNullOrEmpty(_config.OutputDeviceName) ? _config.OutputDeviceId : _config.OutputDeviceName;

        if (ImGui.BeginCombo("##device", currentName))
        {
            if (ImGui.Selectable("System default", string.IsNullOrEmpty(_config.OutputDeviceId)))
            {
                _config.OutputDeviceId = string.Empty;
                _config.OutputDeviceName = string.Empty;
                _config.Save();
                _coordinator.ApplyDeviceChange();
            }
            foreach (var d in _devices)
            {
                if (ImGui.Selectable(d.Name, d.Id == _config.OutputDeviceId))
                {
                    _config.OutputDeviceId = d.Id;
                    _config.OutputDeviceName = d.Name;
                    _config.Save();
                    _coordinator.ApplyDeviceChange();
                }
            }
            ImGui.EndCombo();
        }
        ImGui.TextDisabled("Pick a virtual audio cable here, then add it as a source in OBS.");

        ImGui.Separator();

        var volume = _config.Volume;
        if (ImGui.SliderFloat("Volume", ref volume, 0f, 1f))
        {
            _config.Volume = volume;
            _coordinator.ApplyVolumeChange();
        }
        if (ImGui.IsItemDeactivatedAfterEdit()) _config.Save();

        var loopUntagged = _config.LoopUntaggedTracks;
        if (ImGui.Checkbox("Loop tracks with no loop points", ref loopUntagged))
        {
            _config.LoopUntaggedTracks = loopUntagged;
            _config.Save();
        }

        ImGui.Separator();

        var mute = _config.MuteInGameBgm;
        if (ImGui.Checkbox("Mute the game's own BGM while active", ref mute))
        {
            _config.MuteInGameBgm = mute;
            _config.Save();
            _coordinator.ApplyMuteSettingChange();
        }
        ImGui.TextDisabled("Keeps FFXIV's music out of your main desktop-audio capture so\nonly the separated source carries it.");

        DrawDiagnostics();
    }

    private void DrawDiagnostics()
    {
        ImGui.Separator();
        ImGui.TextUnformatted("Live state");

        var gameSong = _coordinator.GameSongId;
        var pluginSong = _coordinator.CurrentTrackId;
        var scene = _coordinator.GameSceneIndex;
        var battle = _coordinator.IsBattle;

        // A game BGM with no playable file (cutscene/placeholder) should be treated as
        // silence for us, since there's nothing to route.
        var gamePlayable = gameSong != 0 && ScdBgmLoader.GetScdPath(gameSong) != null;
        var effectiveGame = gamePlayable ? gameSong : 0;

        var gameLabel = gameSong == 0 ? "(silence)" : gamePlayable ? gameSong.ToString() : $"{gameSong} (no track)";
        ImGui.TextUnformatted($"Game BGM: {gameLabel}   scene {scene}   {(battle ? "battle" : "field")}");

        var matched = effectiveGame == pluginSong;
        var col = matched ? new Vector4(0.4f, 1f, 0.4f, 1f) : new Vector4(1f, 0.8f, 0.3f, 1f);
        ImGui.TextColored(col, $"Plugin playing: {(pluginSong == 0 ? "(silence)" : pluginSong.ToString())}{(matched ? "  (in sync)" : "  (transitioning)")}");

        ImGui.Separator();
        ImGui.TextUnformatted("Diagnostics (optional, local only)");

        if (_diagnostics.IsRecording)
        {
            if (ImGui.Button("Stop recording"))
            {
                _diagnostics.Stop();
            }
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "Recording BGM state...");
        }
        else
        {
            if (ImGui.Button("Record BGM state"))
            {
                _diagnostics.Start(() => _coordinator.CurrentTrackId);
            }
        }


        if (!string.IsNullOrEmpty(_diagnostics.CurrentPath))
        {
            if (ImGui.SmallButton("Open folder"))
            {
                try
                {
                    var dir = Path.GetDirectoryName(_diagnostics.CurrentPath);
                    if (!string.IsNullOrEmpty(dir))
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Services.Log.Error(ex, "[BgmSeparator] Failed to open diagnostics folder");
                }
            }
            ImGui.SameLine();
            ImGui.TextDisabled(Path.GetFileName(_diagnostics.CurrentPath));
        }
    }

    public void Dispose() { }
}
