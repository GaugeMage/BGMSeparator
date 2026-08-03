using System;
using BgmSeparator.Audio;
using BgmSeparator.Diagnostics;
using BgmSeparator.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace BgmSeparator;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/bgmsep";

    private readonly Configuration _config;
    private readonly BgmPlaybackCoordinator _coordinator;
    private readonly BgmDiagnostics _diagnostics = new();
    private readonly WindowSystem _windows = new("BgmSeparator");
    private readonly ConfigWindow _configWindow;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        Services.Init(pluginInterface);

        _config = Services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        _coordinator = new BgmPlaybackCoordinator(_config);
        _configWindow = new ConfigWindow(_config, _coordinator, _diagnostics);
        _windows.AddWindow(_configWindow);

        Services.Framework.Update += OnFrameworkUpdate;

        Services.PluginInterface.UiBuilder.Draw += _windows.Draw;
        Services.PluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
        Services.PluginInterface.UiBuilder.OpenMainUi += OpenConfig;

        Services.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open settings. \"/bgmsep diag\" toggles BGM state recording (for Ultimate music capture).",
        });

        if (_config.Enabled)
        {
            try { _coordinator.Start(); }
            catch (Exception ex) { Services.Log.Error(ex, "[BgmSeparator] Failed to start on load"); }
        }
    }

    private void OnCommand(string command, string args)
    {
        var arg = args.Trim().ToLowerInvariant();
        if (arg.StartsWith("diag"))
        {
            ToggleDiagnostics(arg);
            return;
        }

        OpenConfig();
    }

    private void ToggleDiagnostics(string arg)
    {
        var wantOn = arg.EndsWith("on") || (!arg.EndsWith("off") && !_diagnostics.IsRecording);
        if (wantOn)
        {
            var path = _diagnostics.Start(() => _coordinator.CurrentTrackId);
            Services.Log.Info($"[BgmSeparator] Recording BGM state to {path}");
            Services.ChatGui.Print($"[BGM Separator] Recording BGM state (optional/local). Run \"/bgmsep diag off\" to stop. File: {path}");
        }
        else
        {
            var path = _diagnostics.CurrentPath;
            _diagnostics.Stop();
            Services.Log.Info("[BgmSeparator] Stopped BGM state recording.");
            Services.ChatGui.Print($"[BGM Separator] Stopped recording. Saved: {path}");
        }
    }

    private void OnFrameworkUpdate(IFramework framework) => _diagnostics.Tick();

    private void OpenConfig() => _configWindow.IsOpen = true;

    public void Dispose()
    {
        Services.Framework.Update -= OnFrameworkUpdate;
        Services.CommandManager.RemoveHandler(CommandName);
        Services.PluginInterface.UiBuilder.Draw -= _windows.Draw;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        Services.PluginInterface.UiBuilder.OpenMainUi -= OpenConfig;
        _windows.RemoveAllWindows();
        _configWindow.Dispose();
        _diagnostics.Dispose();
        _coordinator.Dispose();
    }
}
