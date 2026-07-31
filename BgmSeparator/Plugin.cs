using System;
using BgmSeparator.Audio;
using BgmSeparator.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;

namespace BgmSeparator;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/bgmsep";

    private readonly Configuration _config;
    private readonly BgmPlaybackCoordinator _coordinator;
    private readonly WindowSystem _windows = new("BgmSeparator");
    private readonly ConfigWindow _configWindow;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        Services.Init(pluginInterface);

        _config = Services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        _coordinator = new BgmPlaybackCoordinator(_config);
        _configWindow = new ConfigWindow(_config, _coordinator);
        _windows.AddWindow(_configWindow);

        Services.PluginInterface.UiBuilder.Draw += _windows.Draw;
        Services.PluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
        Services.PluginInterface.UiBuilder.OpenMainUi += OpenConfig;

        Services.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the BGM Separator settings.",
        });

        if (_config.Enabled)
        {
            try { _coordinator.Start(); }
            catch (Exception ex) { Services.Log.Error(ex, "[BgmSeparator] Failed to start on load"); }
        }
    }

    private void OnCommand(string command, string args) => OpenConfig();

    private void OpenConfig() => _configWindow.IsOpen = true;

    public void Dispose()
    {
        Services.CommandManager.RemoveHandler(CommandName);
        Services.PluginInterface.UiBuilder.Draw -= _windows.Draw;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        Services.PluginInterface.UiBuilder.OpenMainUi -= OpenConfig;
        _windows.RemoveAllWindows();
        _configWindow.Dispose();
        _coordinator.Dispose();
    }
}
