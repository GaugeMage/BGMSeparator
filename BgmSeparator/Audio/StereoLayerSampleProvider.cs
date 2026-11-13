using System;
using NAudio.Wave;

namespace BgmSeparator.Audio;

/// <summary>
/// Reduces a layered BGM stream to plain stereo by keeping one channel pair.
///
/// A handful of the game's music files are not stereo: they carry several stereo "layers"
/// interleaved in one 4- or 6-channel Ogg stream, and the game picks which layer is audible
/// (e.g. "Rise" in The Epic of Alexander phase 3 keeps its time-stop "Stasis Loop" on
/// channels 3-4, and field music in Eureka/Bozja keeps battle variants on later pairs).
/// The mixer is stereo, so such a stream has to be narrowed before it can be played at all.
/// Layer 0 (channels 1-2) is the track's normal, non-special arrangement.
/// </summary>
public sealed class StereoLayerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sourceChannels;
    private readonly int _left;
    private readonly int _right;
    private float[] _scratch = Array.Empty<float>();

    public WaveFormat WaveFormat { get; }

    /// <summary>Number of stereo layers in a stream with this many channels.</summary>
    public static int LayerCount(int channels) => Math.Max(1, channels / 2);

    public StereoLayerSampleProvider(ISampleProvider source, int layer = 0)
    {
        _source = source;
        _sourceChannels = source.WaveFormat.Channels;
        if (_sourceChannels < 2)
            throw new ArgumentException("A layered stream needs at least two channels.", nameof(source));

        _left = Math.Clamp(layer, 0, LayerCount(_sourceChannels) - 1) * 2;
        _right = _left + 1;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var frames = count / 2;
        var needed = frames * _sourceChannels;
        if (_scratch.Length < needed) _scratch = new float[needed];

        var framesRead = _source.Read(_scratch, 0, needed) / _sourceChannels;
        for (var f = 0; f < framesRead; f++)
        {
            var src = f * _sourceChannels;
            buffer[offset + f * 2] = _scratch[src + _left];
            buffer[offset + f * 2 + 1] = _scratch[src + _right];
        }

        return framesRead * 2;
    }
}
