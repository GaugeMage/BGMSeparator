using BgmSeparator;
using BgmSeparator.Audio;
using BgmSeparator.Bgm;

var tests = new (string Name, Action Run)[]
{
    ("Chat and macro command parsing", () =>
    {
        foreach (var (args, expected) in new[]
        {
            ("", BgmCommand.Settings), ("  config ", BgmCommand.Settings),
            ("toggle", BgmCommand.Toggle), ("  TOGGLE  ", BgmCommand.Toggle),
            ("on", BgmCommand.Enable), ("enable", BgmCommand.Enable),
            ("off", BgmCommand.Disable), ("disable", BgmCommand.Disable),
            ("diag", BgmCommand.DiagnosticsToggle), (" DIAG \t ON ", BgmCommand.DiagnosticsOn),
            ("diag off", BgmCommand.DiagnosticsOff), ("diagonal", BgmCommand.Help),
            ("diag nonsense", BgmCommand.Help), ("on off", BgmCommand.Help), ("help", BgmCommand.Help),
        }) Check(BgmCommandParser.Parse(args) == expected, $"Unexpected action for '{args}'");
    }),
    ("Startup uses actual state, not saved enabled preference", () =>
    {
        var config = new Configuration { Enabled = true };
        using var coordinator = new BgmPlaybackCoordinator(config);
        Check(!coordinator.IsEnabled, "Not running before startup");
        Check(coordinator.SetEnabled(true), "Starts even with enabled preference");
        Check(AudioEngine.Last.Starts == 1 && Services.GameConfig.System.Muted, "Starts output and mutes game");
    }),
    ("Failed startup rolls back, and on retries successfully", () =>
    {
        var config = new Configuration();
        using var coordinator = new BgmPlaybackCoordinator(config);
        AudioEngine.Last.FailStart = true;
        Check(!coordinator.SetEnabled(true), "Failure must not report enabled");
        Check(!config.Enabled && Services.PluginInterface.SavedEnabled == false, "Disabled state persisted");
        Check(coordinator.LastError != null && !Services.GameConfig.System.Muted, "Actionable error without muting game");
        AudioEngine.Last.FailStart = false;
        Check(coordinator.SetEnabled(true) && coordinator.LastError == null, "Explicit on retries and clears error");
    }),
    ("Repeated on/off and rapid toggles restore original mute", () =>
    {
        foreach (var originallyMuted in new[] { false, true })
        {
            Services.GameConfig.System.Muted = originallyMuted;
            using var coordinator = new BgmPlaybackCoordinator(new Configuration());
            for (var i = 0; i < 20; i++)
            {
                Check(coordinator.ToggleEnabled(), "Toggle enables");
                Check(coordinator.SetEnabled(true), "Repeated on stays enabled");
                Check(!coordinator.ToggleEnabled(), "Toggle disables");
                Check(!coordinator.SetEnabled(false), "Repeated off stays disabled");
                Check(Services.GameConfig.System.Muted == originallyMuted, "Original mute restored");
                Check(!AudioEngine.Last.Running, "Disabled output releases device");
            }
        }
    }),
    ("Changing settings while disabled leaves audio untouched", () =>
    {
        var config = new Configuration { Enabled = false };
        using var coordinator = new BgmPlaybackCoordinator(config);
        coordinator.ApplyDeviceChange();
        coordinator.ApplyMuteSettingChange();
        Check(AudioEngine.Last.Starts == 0 && !Services.GameConfig.System.Muted, "No output or mute while disabled");
    }),
    ("No-mute option never changes game BGM", () =>
    {
        using var coordinator = new BgmPlaybackCoordinator(new Configuration { MuteInGameBgm = false });
        coordinator.SetEnabled(true);
        Check(!Services.GameConfig.System.Muted, "Enabled without muting");
        coordinator.SetEnabled(false);
        Check(!Services.GameConfig.System.Muted, "Disabled without muting");
    }),
    ("Device change failure disables and restores game audio", () =>
    {
        using var coordinator = new BgmPlaybackCoordinator(new Configuration());
        coordinator.SetEnabled(true);
        AudioEngine.Last.FailStart = true;
        coordinator.ApplyDeviceChange();
        Check(!coordinator.IsEnabled && coordinator.LastError != null, "Failed change reports disabled");
        Check(!Services.GameConfig.System.Muted && !AudioEngine.Last.Running, "Output stopped and game restored");
    }),
    ("Mute failures roll back output and remain retryable", () =>
    {
        using var coordinator = new BgmPlaybackCoordinator(new Configuration());
        Services.GameConfig.System.CanRead = false;
        Check(!coordinator.SetEnabled(true) && !AudioEngine.Last.Running, "Unreadable mute aborts startup");
        Services.GameConfig.System.CanRead = true;
        Services.GameConfig.System.FailWrite = true;
        Check(!coordinator.SetEnabled(true) && !AudioEngine.Last.Running, "Mute write failure aborts startup");
        Services.GameConfig.System.FailWrite = false;
        Check(coordinator.SetEnabled(true), "Retry after config recovers");
        Services.GameConfig.System.FailWrite = true;
        coordinator.SetEnabled(false);
        Check(coordinator.LastError != null, "Failed restore is surfaced");
        Services.GameConfig.System.FailWrite = false;
        coordinator.SetEnabled(false);
        Check(!Services.GameConfig.System.Muted && coordinator.LastError == null, "Off retries failed restore");
    }),
    ("Turning off cancels an in-flight track load", () =>
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var coordinator = new BgmPlaybackCoordinator(new Configuration());
        ScdBgmLoader.Load = _ => { entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); return new byte[] { 1 }; };
        coordinator.SetEnabled(true);
        BgmWatcher.Last.Change(123);
        Check(entered.Wait(TimeSpan.FromSeconds(5)), "Decoder entered");
        coordinator.SetEnabled(false);
        release.Set();
        Check(SpinWait.SpinUntil(() => Volatile.Read(ref VorbisLoopSampleProvider.Disposals) > 0, 5000), "Cancelled source disposed");
        Check(coordinator.CurrentTrackId == 0 && AudioEngine.Last.Inputs == 0, "Late decode cannot restart output");
    }),
    ("Dispose restores game BGM", () =>
    {
        var coordinator = new BgmPlaybackCoordinator(new Configuration());
        coordinator.SetEnabled(true);
        coordinator.Dispose();
        Check(!Services.GameConfig.System.Muted && !AudioEngine.Last.Running, "Unload restores output state");
    }),
};

foreach (var (name, run) in tests)
{
    Services.GameConfig.System.Muted = false;
    Services.GameConfig.System.CanRead = true;
    Services.GameConfig.System.FailWrite = false;
    Services.PluginInterface.SavedEnabled = null;
    ScdBgmLoader.Load = _ => null;
    VorbisLoopSampleProvider.Disposals = 0;
    run();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"Passed {tests.Length} regression checks.");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
