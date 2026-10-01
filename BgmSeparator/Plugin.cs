using BgmSeparator.Audio;
using BgmSeparator.Diagnostics;
using BgmSeparator.Input;
using BgmSeparator.Windows;
using Dalamud.Bindings.ImGui;
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
    private readonly HotkeyController _hotkeys;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        Services.Init(pluginInterface);

        _config = Services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        _coordinator = new BgmPlaybackCoordinator(_config);
        _hotkeys = new HotkeyController(_config);
        _configWindow = new ConfigWindow(_config, _coordinator, _diagnostics, _hotkeys.Processor);
        _windows.AddWindow(_configWindow);

        Services.Framework.Update += OnFrameworkUpdate;

        Services.PluginInterface.UiBuilder.Draw += Draw;
        Services.PluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
        Services.PluginInterface.UiBuilder.OpenMainUi += OpenConfig;

        Services.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open settings and configure hotkeys. Use /bgmseptoggle, /bgmsepon, or /bgmsepoff. /bgmsep diag toggles BGM state recording.",
        });
        Services.CommandManager.AddHandler("/bgmseptoggle", new CommandInfo(OnCommand) { HelpMessage = "Toggle separated BGM output." });
        Services.CommandManager.AddHandler("/bgmsepon", new CommandInfo(OnCommand) { HelpMessage = "Enable separated BGM output." });
        Services.CommandManager.AddHandler("/bgmsepoff", new CommandInfo(OnCommand) { HelpMessage = "Disable separated BGM output and restore the previous game BGM setting." });

        if (_config.Enabled)
        {
            _coordinator.SetEnabled(true);
            if (_coordinator.LastError != null)
                Services.ChatGui.PrintError($"[BGM Separator] {_coordinator.LastError}");
        }
    }

    private void OnCommand(string command, string args)
    {
        ExecuteCommand(BgmCommandParser.Parse(command, args));
    }

    private void ExecuteCommand(BgmCommand command)
    {
        switch (command)
        {
            case BgmCommand.Toggle:
                ReportEnabled(_coordinator.ToggleEnabled());
                return;

            case BgmCommand.Enable:
                ReportEnabled(_coordinator.SetEnabled(true));
                return;

            case BgmCommand.Disable:
                ReportEnabled(_coordinator.SetEnabled(false));
                return;
            case BgmCommand.Settings:
                OpenConfig();
                return;
            case BgmCommand.DiagnosticsToggle:
                ToggleDiagnostics(!_diagnostics.IsRecording);
                return;
            case BgmCommand.DiagnosticsOn:
                ToggleDiagnostics(true);
                return;
            case BgmCommand.DiagnosticsOff:
                ToggleDiagnostics(false);
                return;
            default:
                Services.ChatGui.Print("[BGM Separator] /bgmsep opens settings and hotkeys. /bgmseptoggle toggles output; /bgmsepon enables it; /bgmsepoff disables it. /bgmsep diag [on|off] controls local recording.");
                return;
        }
    }

    private void ReportEnabled(bool enabled)
    {
        if (_coordinator.LastError != null)
        {
            Services.ChatGui.PrintError($"[BGM Separator] {_coordinator.LastError}");
            return;
        }

        Services.ChatGui.Print(enabled
            ? _config.MuteInGameBgm
                ? "[BGM Separator] Separated BGM output on (game BGM muted)."
                : "[BGM Separator] Separated BGM output on (game BGM unchanged)."
            : "[BGM Separator] Separated BGM output off (previous game BGM setting restored).");
    }

    private void ToggleDiagnostics(bool wantOn)
    {
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

    private void OnFrameworkUpdate(IFramework framework)
    {
        _diagnostics.Tick();
        if (!_configWindow.IsOpen && _hotkeys.Processor.Capturing != null)
            _hotkeys.Processor.CancelCapture();
        if (_hotkeys.Update() is { } action)
            ExecuteCommand(action switch
            {
                HotkeyAction.Toggle => BgmCommand.Toggle,
                HotkeyAction.On => BgmCommand.Enable,
                _ => BgmCommand.Disable,
            });
    }

    private void Draw()
    {
        _windows.Draw();
        var io = ImGui.GetIO();
        _hotkeys.UiWantsTextInput = io.WantTextInput;
        _hotkeys.UiCapturesKeyboard = io.WantCaptureKeyboard;
    }

    private void OpenConfig() => _configWindow.IsOpen = true;

    public void Dispose()
    {
        Services.Framework.Update -= OnFrameworkUpdate;
        Services.CommandManager.RemoveHandler(CommandName);
        Services.CommandManager.RemoveHandler("/bgmseptoggle");
        Services.CommandManager.RemoveHandler("/bgmsepon");
        Services.CommandManager.RemoveHandler("/bgmsepoff");
        Services.PluginInterface.UiBuilder.Draw -= Draw;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        Services.PluginInterface.UiBuilder.OpenMainUi -= OpenConfig;
        _windows.RemoveAllWindows();
        _configWindow.Dispose();
        _diagnostics.Dispose();
        _coordinator.Dispose();
    }
}
