using System;
using System.IO;
using NAudio.Wave;
using VGAudio.Codecs.CriHca;
using VGAudio.Containers.Hca;

namespace BgmSeparator.Audio;

/// <summary>
/// Decodes an in-memory CRI HCA stream (a .hca file image extracted from an FFXIV .scd,
/// see <see cref="ScdOggExtractor.ExtractHca"/>) and exposes it as an
/// <see cref="ISampleProvider"/> that loops at the points in the HCA header's loop chunk,
/// matching how the game loops BGM.
///
/// Positions in this class are "raw" samples per channel, counted from the first sample
/// the decoder produces for frame 0. That includes the encoder delay the header says to
/// skip, which keeps frame arithmetic simple: raw sample s lives in frame s / 1024.
/// </summary>
public sealed class HcaLoopSampleProvider : ISampleProvider, IDisposable
{
    private const int FrameSamples = 1024; // every HCA frame decodes to 1024 samples per channel
    private const int ChunkFrames = 16;    // frames decoded per refill, about 0.37 s at 44.1 kHz
    private const float ShortToFloat = 1f / 32768f;

    private readonly byte[] _data;
    private readonly int _headerSize;
    private readonly int _frameSize;
    private readonly int _frameCount;
    private readonly int _channels;

    // What the decoder is handed on each refill: a description of the slice of frames, and
    // the frames themselves copied into reusable buffers (one extra for the warm-up frame).
    private readonly HcaInfo _chunkInfo;
    private readonly byte[][] _frameBuffers = new byte[ChunkFrames + 1][];

    private readonly long _end;       // first raw sample past the audible audio
    private readonly long _loopStart;
    private readonly long _loopEnd;   // exclusive
    private readonly bool _loop;

    private short[][] _chunk = Array.Empty<short[]>();
    private long _chunkStart;
    private int _chunkLength;
    private long _position;
    private volatile bool _finished;

    public WaveFormat WaveFormat { get; }

    public HcaLoopSampleProvider(byte[] hcaData, bool loopUntagged)
    {
        HcaStructure file;
        using (var header = new MemoryStream(hcaData, writable: false))
            file = new HcaReader().ReadMetadata(header);
        var info = file.Hca;

        if (info.EncryptionType != 0)
            throw new NotSupportedException($"HCA cipher type {info.EncryptionType} is not supported.");

        _data = hcaData;
        _headerSize = file.HeaderSize;
        _frameSize = info.FrameSize;
        _frameCount = info.FrameCount;
        _channels = info.ChannelCount;

        if (_frameSize < 2 || _frameCount < 1 || _channels < 1
            || (long)_headerSize + (long)_frameCount * _frameSize > hcaData.Length)
            throw new InvalidDataException("HCA stream is truncated or has an invalid header.");

        // Every frame starts with a 0xFFFF sync word. Checking them all here keeps a wrongly
        // descrambled file from reaching the decoder on the audio thread.
        for (var f = 0; f < _frameCount; f++)
        {
            var at = _headerSize + f * _frameSize;
            if (hcaData[at] != 0xFF || hcaData[at + 1] != 0xFF)
                throw new InvalidDataException($"HCA frame {f} has no sync word.");
        }

        for (var i = 0; i < _frameBuffers.Length; i++) _frameBuffers[i] = new byte[_frameSize];

        var total = (long)_frameCount * FrameSamples;
        var start = Math.Clamp((long)info.InsertedSamples, 0, total);
        _end = Math.Clamp(total - info.AppendedSamples, start, total);
        _position = start;

        // The loop chunk names whole frames plus how many samples to trim at each end.
        var loopStart = (long)info.LoopStartFrame * FrameSamples + info.PreLoopSamples;
        var loopEnd = ((long)info.LoopEndFrame + 1) * FrameSamples - info.PostLoopSamples;

        if (info.Looping && loopStart >= start && loopEnd > loopStart && loopEnd <= total)
        {
            _loopStart = loopStart;
            _loopEnd = loopEnd;
            _loop = true;
        }
        else if (loopUntagged && _end > start)
        {
            _loopStart = start;
            _loopEnd = _end;
            _loop = true;
        }

        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(info.SampleRate, _channels);
        _chunkInfo = info;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var wanted = count / _channels; // sample frames
        var done = 0;

        while (done < wanted && !_finished)
        {
            var limit = _loop ? _loopEnd : _end;
            if (_position >= limit)
            {
                if (!_loop)
                {
                    _finished = true;
                    break;
                }
                _position = _loopStart;
                continue;
            }

            if (_position < _chunkStart || _position >= _chunkStart + _chunkLength)
                DecodeChunk((int)(_position / FrameSamples));

            var from = (int)(_position - _chunkStart);
            var n = (int)Math.Min(Math.Min(_chunkStart + _chunkLength, limit) - _position, wanted - done);
            var o = offset + done * _channels;
            for (var c = 0; c < _channels; c++)
            {
                var source = _chunk[c];
                for (var i = 0; i < n; i++)
                    buffer[o + i * _channels + c] = source[from + i] * ShortToFloat;
            }

            _position += n;
            done += n;
        }

        return done * _channels;
    }

    /// <summary>Decodes up to <see cref="ChunkFrames"/> frames starting at <paramref name="frame"/>.</summary>
    private void DecodeChunk(int frame)
    {
        var frames = Math.Min(ChunkFrames, _frameCount - frame);

        // A frame's first 128 samples overlap the tail of the frame before it, so wherever
        // decoding starts mid-stream (after a loop jump too) one extra frame goes in first
        // and its output is thrown away. The result is identical to decoding straight through.
        var warmUp = frame > 0 ? 1 : 0;
        var input = _frameBuffers;
        if (warmUp + frames != input.Length) input = input[..(warmUp + frames)]; // same buffers, shorter list
        for (var i = 0; i < input.Length; i++)
            Buffer.BlockCopy(_data, _headerSize + (frame - warmUp + i) * _frameSize, input[i], 0, _frameSize);

        _chunkInfo.FrameCount = input.Length;
        _chunkInfo.InsertedSamples = warmUp * FrameSamples;
        _chunkInfo.SampleCount = frames * FrameSamples;

        _chunk = CriHcaDecoder.Decode(_chunkInfo, input);
        _chunkStart = (long)frame * FrameSamples;
        _chunkLength = frames * FrameSamples;
    }

    public void Dispose() => _finished = true;
}
