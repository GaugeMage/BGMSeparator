// These stand-ins keep the actual coordinator testable without a running game or speakers.
namespace Dalamud.Configuration
{
    public interface IPluginConfiguration { int Version { get; set; } }
}
namespace Dalamud.Game.Config { }
namespace BgmSeparator
{
    internal static class Services
    {
        public static FakePluginInterface PluginInterface { get; } = new();
        public static FakeGameConfig GameConfig { get; } = new();
        public static FakeLog Log { get; } = new();
    }
    internal sealed class FakePluginInterface
    {
        public bool? SavedEnabled;
        public void SavePluginConfig(Configuration config) => SavedEnabled = config.Enabled;
    }
    internal sealed class FakeGameConfig { public FakeSystemConfig System { get; } = new(); }
    internal sealed class FakeSystemConfig
    {
        public bool Muted;
        public bool CanRead = true;
        public bool FailWrite;
        public bool TryGetBool(string key, out bool value) { value = Muted; return CanRead; }
        public void Set(string key, bool value)
        {
            if (FailWrite) throw new InvalidOperationException("Game config unavailable");
            Muted = value;
        }
    }
    internal sealed class FakeLog { public void Error(Exception ex, string message) { } }
}
namespace NAudio.Wave
{
    public sealed class WaveFormat
    {
        public int Channels => 2;
        public int SampleRate => 48000;
    }
    public interface ISampleProvider { WaveFormat WaveFormat { get; } }
}
namespace NAudio.Wave.SampleProviders
{
    public sealed class MonoToStereoSampleProvider(NAudio.Wave.ISampleProvider source) : NAudio.Wave.ISampleProvider
    {
        public NAudio.Wave.WaveFormat WaveFormat => source.WaveFormat;
    }
    public sealed class WdlResamplingSampleProvider(NAudio.Wave.ISampleProvider source, int rate) : NAudio.Wave.ISampleProvider
    {
        public NAudio.Wave.WaveFormat WaveFormat { get; } = rate > 0 ? source.WaveFormat : throw new ArgumentOutOfRangeException(nameof(rate));
    }
}
namespace BgmSeparator.Bgm
{
    public sealed class BgmWatcher : IDisposable
    {
        public static BgmWatcher Last { get; private set; } = null!;
        public int CurrentSongId { get; private set; }
        public int CurrentSceneIndex => 0;
        public event Action<int, int>? SongChanged;
        public BgmWatcher() => Last = this;
        public void Change(int song)
        {
            var old = CurrentSongId;
            CurrentSongId = song;
            SongChanged?.Invoke(old, song);
        }
        public void Dispose() { }
    }
}
namespace BgmSeparator.Audio
{
    public readonly record struct AudioDeviceInfo(string Id, string Name);
    public sealed class AudioEngine : IDisposable
    {
        public static AudioEngine Last { get; private set; } = null!;
        public static readonly NAudio.Wave.WaveFormat MixFormat = new();
        public bool FailStart;
        public bool Running;
        public int Starts;
        public int Inputs;
        public AudioEngine() => Last = this;
        public static List<AudioDeviceInfo> ListRenderDevices() => new();
        public void Start(string device)
        {
            Starts++;
            if (FailStart) throw new InvalidOperationException("Output unplugged");
            Running = true;
        }
        public void EnsureDevice(string device) { if (!Running) Start(device); }
        public void AddInput(NAudio.Wave.ISampleProvider input) => Inputs++;
        public void RemoveInput(NAudio.Wave.ISampleProvider input) { }
        public void Stop() { Running = false; Inputs = 0; }
        public void Dispose() => Stop();
    }
    public sealed class VorbisLoopSampleProvider(byte[] ogg, bool loop) : NAudio.Wave.ISampleProvider, IDisposable
    {
        public static int Disposals;
        public NAudio.Wave.WaveFormat WaveFormat { get; } = ogg.Length > 0 || loop ? new() : throw new ArgumentException("Empty audio");
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }
    public sealed class FadeSampleProvider(NAudio.Wave.ISampleProvider source, bool startSilent) : NAudio.Wave.ISampleProvider
    {
        public float Volume { get; set; } = startSilent ? 0 : 1;
        public NAudio.Wave.WaveFormat WaveFormat => source.WaveFormat;
        public void BeginFadeIn(int ms) { }
        public void BeginFadeOut(int ms) { }
    }
    public static class ScdBgmLoader
    {
        public static Func<int, byte[]?> Load = _ => null;
        public static byte[]? LoadOgg(int song) => Load(song);
    }
    public static class GameAudioState
    {
        public static bool IsBattleBgm() => false;
        public static bool TryGetSceneFade(int scene, out int fadeIn, out int fadeOut)
        { fadeIn = fadeOut = 1; return true; }
    }
}
