using System;
using System.IO;
using NAudio.Wave;
using NVorbis;

namespace BgmSeparator.Audio;

/// <summary>
/// Decodes an in-memory Ogg Vorbis stream (extracted from an FFXIV .scd) and exposes it
/// as an <see cref="ISampleProvider"/> that loops using the file's LOOPSTART / LOOPEND
/// Vorbis comment tags (sample offsets, per channel), matching how the game loops BGM.
/// </summary>
public sealed class VorbisLoopSampleProvider : ISampleProvider, IDisposable
{
    private readonly MemoryStream _stream;
    private readonly VorbisReader _reader;
    private readonly WaveFormat _waveFormat;
    private readonly int _channels;

    private readonly long _loopStart; // per-channel sample index
    private readonly long _loopEnd;   // per-channel sample index (exclusive)
    private readonly bool _loop;

    private bool _finished;

    public WaveFormat WaveFormat => _waveFormat;

    public VorbisLoopSampleProvider(byte[] oggData, bool loopUntagged)
    {
        _stream = new MemoryStream(oggData, writable: false);
        _reader = new VorbisReader(_stream);
        _channels = _reader.Channels;
        _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_reader.SampleRate, _channels);

        var start = ReadTag("LOOPSTART");
        var end = ReadTag("LOOPEND");

        if (start.HasValue && end.HasValue && end.Value > start.Value)
        {
            _loopStart = start.Value;
            _loopEnd = end.Value;
            _loop = true;
        }
        else if (loopUntagged)
        {
            _loopStart = 0;
            _loopEnd = _reader.TotalSamples;
            _loop = true;
        }
        else
        {
            _loop = false;
        }
    }

    private long? ReadTag(string key)
    {
        try
        {
            var value = _reader.Tags?.GetTagSingle(key);
            if (!string.IsNullOrWhiteSpace(value) && long.TryParse(value, out var samples))
                return samples;
        }
        catch
        {
            // NVorbis tag API differences are non-fatal; fall back to whole-file loop.
        }
        return null;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (_finished) return 0;

        var read = 0;
        while (read < count)
        {
            var wanted = count - read;

            if (_loop)
            {
                // Interleaved samples remaining before we hit the loop end point.
                var framesLeft = (_loopEnd - _reader.SamplePosition) * _channels;
                if (framesLeft <= 0)
                {
                    _reader.SeekTo(_loopStart);
                    continue;
                }
                if (framesLeft < wanted) wanted = (int)framesLeft;
            }

            var n = _reader.ReadSamples(buffer, offset + read, wanted);
            if (n == 0)
            {
                if (_loop)
                {
                    _reader.SeekTo(_loopStart);
                    continue;
                }
                _finished = true;
                break;
            }

            read += n;
        }

        return read;
    }

    public void Dispose()
    {
        _reader.Dispose();
        _stream.Dispose();
    }
}
