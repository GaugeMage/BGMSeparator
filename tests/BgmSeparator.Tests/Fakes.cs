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
    public sealed class WaveFormat(int sampleRate = 48000, int channels = 2)
    {
        public int Channels => channels;
        public int SampleRate => sampleRate;
        public static WaveFormat CreateIeeeFloatWaveFormat(int sampleRate, int channels) => new(sampleRate, channels);
    }
    public interface ISampleProvider
    {
        WaveFormat WaveFormat { get; }
        int Read(float[] buffer, int offset, int count);
    }
}
namespace NAudio.Wave.SampleProviders
{
    public sealed class MonoToStereoSampleProvider(NAudio.Wave.ISampleProvider source) : NAudio.Wave.ISampleProvider
    {
        public NAudio.Wave.WaveFormat WaveFormat { get; } = new(source.WaveFormat.SampleRate, 2);
        public int Read(float[] buffer, int offset, int count) => 0;
    }
    public sealed class WdlResamplingSampleProvider(NAudio.Wave.ISampleProvider source, int rate) : NAudio.Wave.ISampleProvider
    {
        public NAudio.Wave.WaveFormat WaveFormat { get; } = rate > 0 ? new(rate, source.WaveFormat.Channels) : throw new ArgumentOutOfRangeException(nameof(rate));
        public int Read(float[] buffer, int offset, int count) => 0;
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
        public NAudio.Wave.ISampleProvider? LastInput;
        public AudioEngine() => Last = this;
        public static List<AudioDeviceInfo> ListRenderDevices() => new();
        public void Start(string device)
        {
            Starts++;
            if (FailStart) throw new InvalidOperationException("Output unplugged");
            Running = true;
        }
        public void EnsureDevice(string device) { if (!Running) Start(device); }
        // Like the real stereo mixer: anything that is not the mix format is refused.
        public void AddInput(NAudio.Wave.ISampleProvider input)
        {
            if (input.WaveFormat.SampleRate != MixFormat.SampleRate || input.WaveFormat.Channels != MixFormat.Channels)
                throw new ArgumentException("All mixer inputs must have the same WaveFormat");
            LastInput = input;
            Inputs++;
        }
        public void RemoveInput(NAudio.Wave.ISampleProvider input) { }
        public void Stop() { Running = false; Inputs = 0; }
        public void Dispose() => Stop();
    }
    public sealed class VorbisLoopSampleProvider(byte[] ogg, bool loop) : NAudio.Wave.ISampleProvider, IDisposable
    {
        public static int Disposals;
        /// <summary>Channel count the next decoded track reports, e.g. 4 for a layered song.</summary>
        public static int Channels = 2;
        public NAudio.Wave.WaveFormat WaveFormat { get; } = ogg.Length > 0 || loop ? new(44100, Channels) : throw new ArgumentException("Empty audio");
        public int Read(float[] buffer, int offset, int count) => 0;
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }
    public sealed class FadeSampleProvider(NAudio.Wave.ISampleProvider source, bool startSilent) : NAudio.Wave.ISampleProvider
    {
        public float Volume { get; set; } = startSilent ? 0 : 1;
        public NAudio.Wave.WaveFormat WaveFormat => source.WaveFormat;
        public int Read(float[] buffer, int offset, int count) => source.Read(buffer, offset, count);
        public void BeginFadeIn(int ms) { }
        public void BeginFadeOut(int ms) { }
    }
    public static class ScdBgmLoader
    {
        public static Func<int, byte[]?> Load = _ => null;
        // Bytes that start with "HCA" are handed on as an HCA stream, which the real decoder plays.
        public static ScdAudio? LoadTrack(int song) => Load(song) is not { } data ? null : new ScdAudio(
            data.Length > 2 && data[0] == 'H' && data[1] == 'C' && data[2] == 'A' ? ScdAudioFormat.Hca : ScdAudioFormat.OggVorbis, data);
    }
    public static class GameAudioState
    {
        public static bool IsBattleBgm() => false;
        public static bool TryGetSceneFade(int scene, out int fadeIn, out int fadeOut)
        { fadeIn = fadeOut = 1; return true; }
    }
}
