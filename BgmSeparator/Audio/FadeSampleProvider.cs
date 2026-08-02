using System;
using NAudio.Wave;

namespace BgmSeparator.Audio;

/// <summary>
/// A fade + volume provider whose fades ramp linearly in <b>decibels</b> (i.e. exponential
/// amplitude), which matches how FFXIV's BGM fades sound far better than a linear-amplitude
/// fade. A linear-amplitude fade reaches near-full perceived loudness almost immediately,
/// which is why the old crossfade felt like it "played too fast"; a dB-linear fade spends
/// most of its time quiet and eases up gently, like the game's teleport / phase fades.
/// </summary>
public sealed class FadeSampleProvider : ISampleProvider
{
    private enum State { Silent, FadingIn, FullVolume, FadingOut, Faded }

    // Fades run between this floor and 0 dB. -60 dB is inaudible but avoids a hard click.
    private const double FloorDb = -60.0;

    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly object _sync = new();

    private State _state;
    private long _fadePos;      // frames elapsed in the current fade
    private long _fadeLen;      // total frames in the current fade

    /// <summary>User volume multiplier applied on top of the fade gain.</summary>
    public float Volume { get; set; } = 1f;

    /// <summary>True once a fade-out has fully completed (safe to remove from the mixer).</summary>
    public bool IsFadedOut { get { lock (_sync) return _state == State.Faded; } }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public FadeSampleProvider(ISampleProvider source, bool startSilent)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _state = startSilent ? State.Silent : State.FullVolume;
    }

    public void BeginFadeIn(double milliseconds)
    {
        lock (_sync)
        {
            _fadeLen = Math.Max(1, (long)(milliseconds * WaveFormat.SampleRate / 1000.0));
            _fadePos = 0;
            _state = State.FadingIn;
        }
    }

    public void BeginFadeOut(double milliseconds)
    {
        lock (_sync)
        {
            _fadeLen = Math.Max(1, (long)(milliseconds * WaveFormat.SampleRate / 1000.0));
            _fadePos = 0;
            _state = State.FadingOut;
        }
    }

    private static float DbToGain(double db) => (float)Math.Pow(10.0, db / 20.0);

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);

        lock (_sync)
        {
            var i = 0;
            while (i < read)
            {
                float gain;
                switch (_state)
                {
                    case State.Silent:
                    case State.Faded:
                        gain = 0f;
                        break;
                    case State.FullVolume:
                        gain = 1f;
                        break;
                    case State.FadingIn:
                    {
                        var p = (double)_fadePos / _fadeLen;
                        if (p >= 1.0) { _state = State.FullVolume; gain = 1f; }
                        else gain = DbToGain(FloorDb * (1.0 - p)); // -60 dB -> 0 dB
                        break;
                    }
                    case State.FadingOut:
                    {
                        var p = (double)_fadePos / _fadeLen;
                        if (p >= 1.0) { _state = State.Faded; gain = 0f; }
                        else gain = DbToGain(FloorDb * p);          // 0 dB -> -60 dB
                        break;
                    }
                    default:
                        gain = 1f;
                        break;
                }

                gain *= Volume;
                for (var ch = 0; ch < _channels; ch++)
                    buffer[offset + i++] *= gain;

                if (_state == State.FadingIn || _state == State.FadingOut)
                    _fadePos++;
            }
        }

        return read;
    }
}
