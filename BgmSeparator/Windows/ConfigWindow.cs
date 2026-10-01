using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using BgmSeparator.Audio;
using BgmSeparator.Diagnostics;
using BgmSeparator.Input;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace BgmSeparator.Windows;

internal sealed class ConfigWindow : Window, IDisposable
{
    private readonly Configuration _config;
    private readonly BgmPlaybackCoordinator _coordinator;
    private readonly BgmDiagnostics _diagnostics;
    private readonly HotkeyProcessor _hotkeys;

    private List<AudioDeviceInfo> _devices = new();

    public ConfigWindow(Configuration config, BgmPlaybackCoordinator coordinator, BgmDiagnostics diagnostics, HotkeyProcessor hotkeys)
        : base("BGM Separator###BgmSeparatorConfig")
    {
        _config = config;
        _coordinator = coordinator;
        _diagnostics = diagnostics;
        _hotkeys = hotkeys;
        Size = new Vector2(520, 620);
        SizeCondition = ImGuiCond.FirstUseEver;
        RefreshDevices();
    }

    private void RefreshDevices() => _devices = BgmPlaybackCoordinator.ListDevices();

    public override void Draw()
    {
        var enabled = _coordinator.IsEnabled;
        if (ImGui.Checkbox("Enable separated BGM output", ref enabled))
            _coordinator.SetEnabled(enabled);
        ImGui.TextWrapped("Commands: /bgmseptoggle, /bgmsepon, /bgmsepoff");
        if (_coordinator.LastError != null)
            ImGui.TextWrapped(_coordinator.LastError);

        DrawHotkeys();

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

    private void DrawHotkeys()
    {
        ImGui.Separator();
        ImGui.TextUnformatted("Keybinds");
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            ImGui.PushID(action.ToString());
            ImGui.TextUnformatted(action.ToString());
            ImGui.SameLine(80);
            var capturing = _hotkeys.Capturing == action;
            if (ImGui.Button(capturing ? "Press a key..." : HotkeyController.Display(_hotkeys.GetBinding(action)), new Vector2(220, 0)))
                _hotkeys.BeginCapture(action);
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear")) _hotkeys.SetBinding(action, new HotkeyBinding());
            ImGui.PopID();
        }
        if (_hotkeys.Capturing != null)
        {
            ImGui.TextWrapped("Press your shortcut, optionally holding Ctrl, Alt, or Shift. Escape cancels.");
            if (ImGui.Button("Cancel binding")) _hotkeys.CancelCapture();
        }
        if (_hotkeys.CaptureError != null) ImGui.TextWrapped(_hotkeys.CaptureError);
        ImGui.TextWrapped("Click a binding to change it. Shortcuts work while FFXIV is focused and you are not typing. Choose keys not already used by your game controls.");
        RespectCloseHotkey = _hotkeys.Capturing == null;
    }

    public override void OnClose() => _hotkeys.CancelCapture();

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
        ImGui.TextColored(col, !_coordinator.IsEnabled
            ? "Plugin playing: (disabled)"
            : $"Plugin playing: {(pluginSong == 0 ? "(silence)" : pluginSong.ToString())}{(matched ? "  (in sync)" : "  (transitioning)")}");

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
