using System;
using System.Collections.Generic;
using System.Numerics;
using BgmSeparator.Audio;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace BgmSeparator.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Configuration _config;
    private readonly BgmPlaybackCoordinator _coordinator;

    private List<AudioDeviceInfo> _devices = new();

    public ConfigWindow(Configuration config, BgmPlaybackCoordinator coordinator)
        : base("BGM Separator###BgmSeparatorConfig")
    {
        _config = config;
        _coordinator = coordinator;
        Size = new Vector2(460, 340);
        SizeCondition = ImGuiCond.FirstUseEver;
        RefreshDevices();
    }

    private void RefreshDevices() => _devices = BgmPlaybackCoordinator.ListDevices();

    public override void Draw()
    {
        var enabled = _config.Enabled;
        if (ImGui.Checkbox("Enable separated BGM output", ref enabled))
        {
            _config.Enabled = enabled;
            _config.Save();
            if (enabled) _coordinator.Start();
            else _coordinator.Stop();
        }

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

        var fadeIn = _config.FadeInMs;
        if (ImGui.SliderInt("Fade in (ms)", ref fadeIn, 0, 12000))
            _config.FadeInMs = fadeIn;
        if (ImGui.IsItemDeactivatedAfterEdit()) _config.Save();

        var fadeOut = _config.FadeOutMs;
        if (ImGui.SliderInt("Fade out (ms)", ref fadeOut, 0, 12000))
            _config.FadeOutMs = fadeOut;
        if (ImGui.IsItemDeactivatedAfterEdit()) _config.Save();
        ImGui.TextDisabled("Fades ramp in decibels, like the game. Zone/teleport fade-ins\nare long in FFXIV, so try 4000-8000 ms to match.");

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
    }

    public void Dispose() { }
}
