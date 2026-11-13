using BgmSeparator;
using BgmSeparator.Audio;
using BgmSeparator.Bgm;
using BgmSeparator.Input;
using VGAudio.Containers.Hca;
using VGAudio.Formats.Pcm16;

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
    ("Layered multi-channel tracks play as stereo instead of going silent", () =>
    {
        // 725 is "Rise" in The Epic of Alexander phase 3: four channels, its time-stop loop on 3-4.
        foreach (var channels in new[] { 4, 6, 1, 2 })
        {
            VorbisLoopSampleProvider.Channels = channels;
            ScdBgmLoader.Load = _ => new byte[] { 1 };
            using var coordinator = new BgmPlaybackCoordinator(new Configuration());
            coordinator.SetEnabled(true);
            BgmWatcher.Last.Change(725);
            Check(SpinWait.SpinUntil(() => AudioEngine.Last.Inputs == 1, 5000), $"{channels}-channel track reaches the mixer");
            Check(coordinator.CurrentTrackId == 725, $"{channels}-channel track is the one playing");
            var format = AudioEngine.Last.LastInput!.WaveFormat;
            Check(format.Channels == 2 && format.SampleRate == AudioEngine.MixFormat.SampleRate, "Mixer gets the stereo mix format");
        }
    }),
    ("Stereo layer keeps exactly one channel pair", () =>
    {
        // Frame f of a 4-channel stream holds f*10+1 .. f*10+4.
        var fourChannel = new float[4 * 5];
        for (var i = 0; i < fourChannel.Length; i++) fourChannel[i] = i / 4 * 10 + i % 4 + 1;

        var main = new float[12];
        var layer0 = new StereoLayerSampleProvider(new ArraySource(fourChannel, 4));
        Check(layer0.WaveFormat.Channels == 2 && layer0.WaveFormat.SampleRate == 44100, "Output is stereo at the source rate");
        Check(layer0.Read(main, 1, 10) == 10, "Reads all five frames");
        Check(main.SequenceEqual(new float[] { 0, 1, 2, 11, 12, 21, 22, 31, 32, 41, 42, 0 }), "Default layer is channels 1-2, written at the offset");
        Check(layer0.Read(main, 0, 10) == 0, "End of stream");

        var special = new float[4];
        var layer1 = new StereoLayerSampleProvider(new ArraySource(fourChannel, 4, maxPerRead: 4), layer: 1);
        Check(layer1.Read(special, 0, 4) == 2 && special[0] == 3 && special[1] == 4, "Short source reads return whole frames of channels 3-4");

        var clamped = new float[2];
        new StereoLayerSampleProvider(new ArraySource(fourChannel, 4), layer: 9).Read(clamped, 0, 2);
        Check(clamped[0] == 3 && clamped[1] == 4, "Out-of-range layer clamps to the last pair");

        var six = new float[] { 1, 2, 3, 4, 5, 6 };
        var sixOut = new float[2];
        new StereoLayerSampleProvider(new ArraySource(six, 6), layer: 2).Read(sixOut, 0, 2);
        Check(StereoLayerSampleProvider.LayerCount(6) == 3 && sixOut[0] == 5 && sixOut[1] == 6, "Six channels are three layers");
    }),
    ("SCD format 26 is extracted as HCA with its frames descrambled", () =>
    {
        // The first bytes of frame 0 of Gridania's day and night themes as the game files store
        // them (streams of 9141528 / 6045930 bytes after HCA headers of 160 / 480 bytes), and
        // what they must become: every HCA frame opens with the sync word FF FF.
        foreach (var (streamSize, headerSize, stored, plain) in new[]
        {
            (9141528, 160, "EC32F4D390687C4FA29918A7", "FFFF428A747BB1F9FB7D3BCF"),
            (6045930, 480, "37F25858485858387C7156CB", "FFFF00000000000000000000"),
        })
        {
            var frames = new byte[streamSize];
            Convert.FromHexString(stored).CopyTo(frames, 0);
            ScdOggExtractor.XorHcaFrames(frames, headerSize);
            Check(Convert.ToHexString(frames, 0, 12) == plain, "Real game bytes descramble to a frame sync word");
        }

        var audio = ScdOggExtractor.ExtractAudio(HcaFixture.Scd);
        Check(audio is { Format: ScdAudioFormat.Hca }, "Format 26 is recognised as HCA");
        Check(audio!.Data.SequenceEqual(HcaFixture.Hca), "Header is kept as stored and the frames come out descrambled");
        Check(ScdOggExtractor.Extract(HcaFixture.Scd) == null, "The Ogg extractor still leaves it alone");

        var otherFormat = (byte[])HcaFixture.Scd.Clone();
        otherFormat[HcaFixture.ScdEntryOffset + 12] = 12; // MS ADPCM, used by sound effects
        Check(ScdOggExtractor.ExtractAudio(otherFormat) == null, "Formats without a decoder are still skipped");
    }),
    ("HCA track decodes to the source audio and loops at its loop points", () =>
    {
        using var provider = new HcaLoopSampleProvider(HcaFixture.Hca, loopUntagged: false);
        Check(provider.WaveFormat.Channels == 2 && provider.WaveFormat.SampleRate == HcaFixture.SampleRate, "Stereo at the file's rate");

        // Play past the loop end twice, in uneven reads that straddle every decode boundary.
        int loopStart = HcaFixture.LoopStart, loopEnd = HcaFixture.LoopEnd, loopLength = loopEnd - loopStart;
        var output = new float[(loopEnd + 2 * loopLength + 3000) * 2];
        for (int read = 0, size = 1; read < output.Length;)
        {
            var ask = Math.Min(2 * (size = size * 7 % 4099 + 1), output.Length - read);
            Check(provider.Read(output, read, ask) == ask, "A looping track never runs dry");
            read += ask;
        }

        // HCA is lossy, so compare with the source within a tolerance. The source is a pair of
        // sweeps, so being even one sample off at either loop point lands far outside it.
        double Error(int from, int count, int slip = 0)
        {
            double difference = 0, signal = 0;
            for (var n = from; n < from + count; n++)
            {
                var source = (n < loopEnd ? n : loopStart + (n - loopEnd) % loopLength) + slip;
                for (var c = 0; c < 2; c++)
                {
                    var expected = HcaFixture.Pcm[c][source] / 32768.0;
                    difference += Math.Pow(output[n * 2 + c] - expected, 2);
                    signal += expected * expected;
                }
            }
            return Math.Sqrt(difference / signal);
        }
        Check(Error(2000, 20000) < 0.05, "Start of the track matches the source, left and right the right way round");
        Check(Error(loopEnd - 4000, 4000) < 0.05, "Plays right up to the loop end");
        Check(Error(loopEnd, 4000) < 0.05, "The sample after the loop end is the loop start");
        Check(Error(loopEnd + loopLength - 2000, 4000) < 0.05, "The second pass loops at the same points");
        Check(Error(loopEnd, 4000, slip: 1) > 0.2 && Error(loopEnd, 4000, slip: -1) > 0.2, "The tolerance would catch a one-sample slip");
    }),
    ("HCA track without loop points ends once, or loops whole when asked to", () =>
    {
        var total = HcaFixture.Pcm[0].Length;
        var buffer = new float[(total + 1000) * 2];

        using var once = new HcaLoopSampleProvider(HcaFixture.HcaNoLoop, loopUntagged: false);
        Check(once.Read(buffer, 0, buffer.Length) == total * 2, "Plays exactly the track's samples, without the encoder padding");
        Check(once.Read(buffer, 0, buffer.Length) == 0, "Then ends");

        using var whole = new HcaLoopSampleProvider(HcaFixture.HcaNoLoop, loopUntagged: true);
        var firstFrames = new float[2000];
        whole.Read(firstFrames, 0, 2000);
        whole.Read(buffer, 0, (total - 1000) * 2);
        var afterWrap = new float[2000];
        Check(whole.Read(afterWrap, 0, 2000) == 2000 && afterWrap.SequenceEqual(firstFrames), "Wraps from the last sample to the first");

        whole.Dispose();
        Check(whole.Read(buffer, 0, 2000) == 0, "A disposed track stops producing audio");
    }),
    ("Damaged HCA streams are refused before they reach playback", () =>
    {
        var badFrame = (byte[])HcaFixture.Hca.Clone();
        badFrame[HcaFixture.HeaderSize + 5 * HcaFixture.FrameSize] = 0; // frame 5 loses its sync word
        Check(Throws<InvalidDataException>(() => new HcaLoopSampleProvider(badFrame, false)), "A stream that was not descrambled correctly");
        Check(Throws<InvalidDataException>(() => new HcaLoopSampleProvider(HcaFixture.Hca[..^HcaFixture.FrameSize], false)), "A truncated stream");
    }),
    ("HCA tracks reach the mixer through the real decoder", () =>
    {
        // 6 and 7 are Gridania's day and night themes, the two BGM files stored as HCA.
        foreach (var song in new[] { 6, 7 })
        {
            ScdBgmLoader.Load = _ => HcaFixture.Hca;
            using var coordinator = new BgmPlaybackCoordinator(new Configuration());
            coordinator.SetEnabled(true);
            BgmWatcher.Last.Change(song);
            Check(SpinWait.SpinUntil(() => AudioEngine.Last.Inputs == 1, 5000), "HCA track reaches the mixer");
            Check(coordinator.CurrentTrackId == song, "HCA track is the one playing");
            var format = AudioEngine.Last.LastInput!.WaveFormat;
            Check(format.Channels == 2 && format.SampleRate == AudioEngine.MixFormat.SampleRate, "Mixer gets the stereo mix format");

            // Stopping disposes whichever decoder was playing the track.
            coordinator.SetEnabled(false);
            Check(Volatile.Read(ref VorbisLoopSampleProvider.Disposals) == 0, "It was not handed to the Vorbis decoder");
        }
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
    VorbisLoopSampleProvider.Channels = 2;
    run();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"Passed {tests.Length} regression checks.");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static bool Throws<T>(Action action) where T : Exception
{
    try { action(); return false; }
    catch (T) { return true; }
}

static bool[] Keys(params int[] down)
{
    var keys = new bool[256];
    foreach (var key in down) keys[key] = true;
    return keys;
}

/// <summary>Interleaved test audio with a fixed channel count.</summary>
sealed class ArraySource(float[] samples, int channels, int maxPerRead = int.MaxValue) : NAudio.Wave.ISampleProvider
{
    private int _position;
    public NAudio.Wave.WaveFormat WaveFormat { get; } = new(44100, channels);
    public int Read(float[] buffer, int offset, int count)
    {
        var n = Math.Min(Math.Min(count, maxPerRead), samples.Length - _position);
        Array.Copy(samples, _position, buffer, offset, n);
        _position += n;
        return n;
    }
}

/// <summary>
/// A four-second looping HCA track made with the encoder, and the same track wrapped in an
/// .scd the way the game stores format 26 music (see BGM_Town_Gri_Day.scd).
/// </summary>
static class HcaFixture
{
    public const int SampleRate = 44100, LoopStart = 40000, LoopEnd = 150001, ScdEntryOffset = 0x80;
    public static readonly short[][] Pcm = MakeSweeps();
    public static readonly byte[] Hca = new HcaWriter().GetFile(new Pcm16FormatBuilder(Pcm, SampleRate).WithLoop(true, LoopStart, LoopEnd).Build());
    public static readonly byte[] HcaNoLoop = new HcaWriter().GetFile(new Pcm16FormatBuilder(Pcm, SampleRate).Build());
    private static readonly HcaStructure Header = new HcaReader().ReadMetadata(new MemoryStream(Hca));
    public static readonly int HeaderSize = Header.HeaderSize, FrameSize = Header.Hca.FrameSize;
    public static readonly byte[] Scd = WrapInScd();

    // Left sweeps up and right sweeps down, so no two positions in the track sound alike.
    private static short[][] MakeSweeps()
    {
        var pcm = new[] { new short[SampleRate * 4], new short[SampleRate * 4] };
        for (var i = 0; i < pcm[0].Length; i++)
        {
            var t = i / (double)SampleRate;
            pcm[0][i] = (short)(12000 * Math.Sin(2 * Math.PI * (2000 * t + 500 * t * t)));
            pcm[1][i] = (short)(12000 * Math.Sin(2 * Math.PI * (6000 * t - 500 * t * t)));
        }
        return pcm;
    }

    private static byte[] WrapInScd()
    {
        var frames = Hca[HeaderSize..];
        ScdOggExtractor.XorHcaFrames(frames, HeaderSize); // XOR is its own inverse: this scrambles

        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("SEDBSSCF"u8);
        w.Write(new byte[40]);                         // rest of the 48-byte file header
        w.Write((ushort)1); w.Write((ushort)1);        // sound and track counts
        w.Write((ushort)1); w.Write((ushort)9999);     // audio count, number
        w.Write(0x60u); w.Write(0x70u);                // track table, audio table
        w.Write(new byte[0x70 - 0x40]);
        w.Write((uint)ScdEntryOffset);                 // audio table: entry 0
        w.Write(new byte[ScdEntryOffset - 0x74]);

        var hca = Header.Hca;
        w.Write((uint)frames.Length); w.Write(2u); w.Write((uint)SampleRate); w.Write(26u);
        w.Write((uint)hca.LoopStartFrame); w.Write((uint)hca.LoopEndFrame);
        w.Write((uint)(0x18 + HeaderSize)); w.Write(0u);

        w.Write((byte)2); w.Write((byte)0x18);         // extra data: version, its own size
        w.Write((ushort)HeaderSize); w.Write((ushort)FrameSize);
        w.Write(new byte[4]);
        w.Write((ushort)hca.InsertedSamples);
        w.Write(new byte[4]);
        w.Write((uint)hca.LoopStartFrame); w.Write((uint)hca.LoopEndFrame);

        w.Write(Hca, 0, HeaderSize);
        w.Write(frames);
        w.Flush();
        return stream.ToArray();
    }
}
