using BgmSeparator;
using BgmSeparator.Audio;
using BgmSeparator.Bgm;
using BgmSeparator.Input;

var tests = new (string Name, Action Run)[]
{
    ("Chat command parsing", () =>
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
    ("One-word commands and legacy aliases", () =>
    {
        Check(BgmCommandParser.Parse("/bgmseptoggle", "") == BgmCommand.Toggle, "One-word toggle");
        Check(BgmCommandParser.Parse("/BGMSEPON", " ") == BgmCommand.Enable, "Case insensitive on");
        Check(BgmCommandParser.Parse("/bgmsepoff", "") == BgmCommand.Disable, "One-word off");
        Check(BgmCommandParser.Parse("/bgmsepon", "off") == BgmCommand.Help, "Unexpected arguments must not act");
        Check(BgmCommandParser.Parse("/bgmsep", "toggle") == BgmCommand.Toggle, "Existing macros keep working");
        Check(BgmCommandParser.Parse("/bgm", "") == BgmCommand.Help, "Do not handle the game's command");
    }),
    ("All three hotkeys fire once per press", () =>
    {
        var config = new Configuration
        {
            ToggleHotkey = new() { Key = 112 }, OnHotkey = new() { Key = 113 }, OffHotkey = new() { Key = 114 },
        };
        var input = new HotkeyProcessor(config);
        input.Update(Keys(), false, false);
        foreach (var (key, action) in new[] { (112, HotkeyAction.Toggle), (113, HotkeyAction.On), (114, HotkeyAction.Off) })
        {
            Check(input.Update(Keys(key), false, false) == action, "Correct action on press");
            for (var i = 0; i < 20; i++) Check(input.Update(Keys(key), false, false) == null, "Held key does not repeat");
            input.Update(Keys(), false, false);
            Check(input.Update(Keys(key), false, false) == action, "Repress fires again");
            input.Update(Keys(), false, false);
        }
    }),
    ("Hotkeys require exact modifiers and a fresh primary key press", () =>
    {
        var input = new HotkeyProcessor(new Configuration
        {
            ToggleHotkey = new() { Key = 75, Ctrl = true, Shift = true }, OffHotkey = new() { Key = 75 },
        });
        input.Update(Keys(), false, false);
        Check(input.Update(Keys(75, 17), false, false) == null, "Missing Shift ignored");
        Check(input.Update(Keys(75, 17, 16), false, false) == null, "Adding modifier to held key does not fire");
        input.Update(Keys(), false, false);
        Check(input.Update(Keys(75, 17, 16, 18), false, false) == null, "Extra Alt ignored");
        input.Update(Keys(), false, false);
        Check(input.Update(Keys(75, 17, 16), false, false) == HotkeyAction.Toggle, "Exact combination fires");
        Check(input.Update(Keys(75), false, false) == null, "Releasing modifiers cannot trigger another action");
    }),
    ("Typing and lost focus require release before rearming", () =>
    {
        var input = new HotkeyProcessor(new Configuration { ToggleHotkey = new() { Key = 75 } });
        input.Update(Keys(), false, false);
        Check(input.Update(Keys(75), true, false) == null, "Blocked input ignored");
        Check(input.Update(Keys(75), false, false) == null, "Held key after focus returns ignored");
        input.Update(Keys(), false, false);
        Check(input.Update(Keys(75), false, true) == null, "Plugin keyboard interaction ignored");
        Check(input.Update(Keys(75), false, false) == null, "Held key after UI interaction ignored");
        input.Update(Keys(), false, false);
        Check(input.Update(Keys(75), false, false) == HotkeyAction.Toggle, "Fresh press works");
    }),
    ("Capture saves modifiers without firing and supports clear", () =>
    {
        var config = new Configuration();
        var input = new HotkeyProcessor(config);
        input.BeginCapture(HotkeyAction.On);
        Check(input.Update(Keys(13), false, true) == null, "Ignore held key that opened capture");
        input.Update(Keys(17, 18), false, true);
        Check(input.Update(Keys(17, 18, 116), false, true) == null, "Capture does not execute");
        Check(input.Capturing == null && config.OnHotkey.Key == 116 && config.OnHotkey.Ctrl && config.OnHotkey.Alt, "Binding saved");
        Check(Services.PluginInterface.SavedEnabled != null, "Config persisted");
        Check(input.Update(Keys(17, 18, 116), false, false) == null, "Captured key still held does not execute");
        input.Update(Keys(), false, false);
        Check(input.Update(Keys(17, 18, 116), false, false) == HotkeyAction.On, "Assigned hotkey works");
        input.SetBinding(HotkeyAction.On, new());
        Check(config.OnHotkey.Key == 0, "Clear removes binding");
    }),
    ("Capture cancels and rejects duplicate bindings", () =>
    {
        var config = new Configuration { ToggleHotkey = new() { Key = 112 } };
        var input = new HotkeyProcessor(config);
        input.BeginCapture(HotkeyAction.Off);
        input.Update(Keys(), false, false);
        input.Update(Keys(112), false, false);
        Check(input.CaptureError != null && input.Capturing == HotkeyAction.Off && config.OffHotkey.Key == 0, "Duplicate rejected");
        input.Update(Keys(), false, false);
        input.Update(Keys(113), false, false);
        Check(config.OffHotkey.Key == 113 && input.CaptureError == null, "Unique binding accepted after duplicate");
        input.BeginCapture(HotkeyAction.Off);
        input.Update(Keys(), false, false);
        input.Update(Keys(27), false, false);
        Check(input.Capturing == null && config.OffHotkey.Key == 113, "Escape preserves old binding");
        input.BeginCapture(HotkeyAction.Off);
        input.Update(Keys(), true, false);
        Check(input.Capturing == null, "Focus loss cancels capture");
    }),
    ("Default settings, invalid keys, and simultaneous shortcuts are safe", () =>
    {
        var config = new Configuration();
        var input = new HotkeyProcessor(config);
        input.Update(Keys(), false, false);
        Check(input.Update(Keys(65), false, false) == null, "No default shortcuts");
        config.ToggleHotkey.Key = 999;
        Check(input.Update(Keys(65), false, false) == null, "Invalid saved key does not crash");
        config.ToggleHotkey.Key = 112;
        config.OffHotkey.Key = 113;
        input.Update(Keys(), false, false);
        Check(input.Update(Keys(112, 113), false, false) == HotkeyAction.Off, "Only one action, Off wins");
    }),
    ("Hotkey settings survive serialization and older configs stay unbound", () =>
    {
        var oldConfig = System.Text.Json.JsonSerializer.Deserialize<Configuration>("{\"Enabled\":false}")!;
        Check(oldConfig.ToggleHotkey.Key == 0 && oldConfig.OnHotkey.Key == 0 && oldConfig.OffHotkey.Key == 0, "Older config has no surprise bindings");
        oldConfig.ToggleHotkey = new() { Key = 75, Ctrl = true, Alt = true, Shift = true };
        oldConfig.OnHotkey = new() { Key = 112 };
        oldConfig.OffHotkey = new() { Key = 113, Alt = true };
        var restored = System.Text.Json.JsonSerializer.Deserialize<Configuration>(System.Text.Json.JsonSerializer.Serialize(oldConfig))!;
        Check(restored.ToggleHotkey.SameAs(oldConfig.ToggleHotkey) && restored.OnHotkey.SameAs(oldConfig.OnHotkey)
            && restored.OffHotkey.SameAs(oldConfig.OffHotkey), "All bindings and modifiers persist");
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

static bool[] Keys(params int[] down)
{
    var keys = new bool[256];
    foreach (var key in down) keys[key] = true;
    return keys;
}
