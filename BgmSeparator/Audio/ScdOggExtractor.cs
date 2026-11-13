using System;
using System.IO;

namespace BgmSeparator.Audio;

/// <summary>
/// Extracts the Ogg Vorbis stream (or, for the few files that use it, the HCA stream) from
/// a raw FFXIV .scd file.
///
/// This reimplements Lumina's <c>ScdFile.GetAudio()</c> because the version of Lumina
/// bundled with Dalamud still has the known "Source array was not long enough" bug
/// (SubInfoSize includes the Ogg header size, over-running the read position). We apply
/// the corrected offset here: subInfoStartPos + SubInfoSize - OggHeaderSize.
/// Layouts/XOR table are from NotAdam/Lumina (master).
/// </summary>
public static class ScdOggExtractor
{
    private const int BinaryHeaderSize = 48;
    private const int AudioBasicDescSize = 32;
    private const int OggSeekHeaderSize = 32;
    private const uint MarkerChunkFlag = 0x01;
    private const int AudioFormatOggVorbis = 6;
    private const int AudioFormatHca = 26;

    // Descramble table used for scd version 3 Ogg headers (from FFXIV Explorer / Lumina).
    private static readonly byte[] OggXorTable =
    {
        0x3A, 0x32, 0x32, 0x32, 0x03, 0x7E, 0x12, 0xF7, 0xB2, 0xE2, 0xA2, 0x67, 0x32, 0x32, 0x22, 0x32,
        0x32, 0x52, 0x16, 0x1B, 0x3C, 0xA1, 0x54, 0x7B, 0x1B, 0x97, 0xA6, 0x93, 0x1A, 0x4B, 0xAA, 0xA6,
        0x7A, 0x7B, 0x1B, 0x97, 0xA6, 0xF7, 0x02, 0xBB, 0xAA, 0xA6, 0xBB, 0xF7, 0x2A, 0x51, 0xBE, 0x03,
        0xF4, 0x2A, 0x51, 0xBE, 0x03, 0xF4, 0x2A, 0x51, 0xBE, 0x12, 0x06, 0x56, 0x27, 0x32, 0x32, 0x36,
        0x32, 0xB2, 0x1A, 0x3B, 0xBC, 0x91, 0xD4, 0x7B, 0x58, 0xFC, 0x0B, 0x55, 0x2A, 0x15, 0xBC, 0x40,
        0x92, 0x0B, 0x5B, 0x7C, 0x0A, 0x95, 0x12, 0x35, 0xB8, 0x63, 0xD2, 0x0B, 0x3B, 0xF0, 0xC7, 0x14,
        0x51, 0x5C, 0x94, 0x86, 0x94, 0x59, 0x5C, 0xFC, 0x1B, 0x17, 0x3A, 0x3F, 0x6B, 0x37, 0x32, 0x32,
        0x30, 0x32, 0x72, 0x7A, 0x13, 0xB7, 0x26, 0x60, 0x7A, 0x13, 0xB7, 0x26, 0x50, 0xBA, 0x13, 0xB4,
        0x2A, 0x50, 0xBA, 0x13, 0xB5, 0x2E, 0x40, 0xFA, 0x13, 0x95, 0xAE, 0x40, 0x38, 0x18, 0x9A, 0x92,
        0xB0, 0x38, 0x00, 0xFA, 0x12, 0xB1, 0x7E, 0x00, 0xDB, 0x96, 0xA1, 0x7C, 0x08, 0xDB, 0x9A, 0x91,
        0xBC, 0x08, 0xD8, 0x1A, 0x86, 0xE2, 0x70, 0x39, 0x1F, 0x86, 0xE0, 0x78, 0x7E, 0x03, 0xE7, 0x64,
        0x51, 0x9C, 0x8F, 0x34, 0x6F, 0x4E, 0x41, 0xFC, 0x0B, 0xD5, 0xAE, 0x41, 0xFC, 0x0B, 0xD5, 0xAE,
        0x41, 0xFC, 0x3B, 0x70, 0x71, 0x64, 0x33, 0x32, 0x12, 0x32, 0x32, 0x36, 0x70, 0x34, 0x2B, 0x56,
        0x22, 0x70, 0x3A, 0x13, 0xB7, 0x26, 0x60, 0xBA, 0x1B, 0x94, 0xAA, 0x40, 0x38, 0x00, 0xFA, 0xB2,
        0xE2, 0xA2, 0x67, 0x32, 0x32, 0x12, 0x32, 0xB2, 0x32, 0x32, 0x32, 0x32, 0x75, 0xA3, 0x26, 0x7B,
        0x83, 0x26, 0xF9, 0x83, 0x2E, 0xFF, 0xE3, 0x16, 0x7D, 0xC0, 0x1E, 0x63, 0x21, 0x07, 0xE3, 0x01,
    };

    /// <summary>Returns descrambled Ogg Vorbis bytes for audio entry 0, or null if not Ogg.</summary>
    public static byte[]? Extract(byte[] scd)
    {
        using var ms = new MemoryStream(scd, writable: false);
        using var r = new BinaryReader(ms);

        // ScdHeader lives directly after the 48-byte BinaryHeader.
        ms.Position = BinaryHeaderSize;
        r.ReadUInt16();                        // SoundCount
        r.ReadUInt16();                        // TrackCount
        var audioCount = r.ReadUInt16();       // AudioCount
        r.ReadUInt16();                        // Number
        r.ReadUInt32();                        // TrackOffset
        var audioOffset = r.ReadUInt32();      // AudioOffset

        if (audioCount == 0) return null;

        ms.Position = audioOffset;
        var firstAudioOffset = r.ReadUInt32(); // offset table -> entry 0

        // AudioBasicDesc
        ms.Position = firstAudioOffset;
        var size = r.ReadUInt32();
        r.ReadUInt32();                        // Channel
        r.ReadUInt32();                        // Rate
        var format = (int)r.ReadUInt32();      // Format
        r.ReadUInt32();                        // LoopStart
        r.ReadUInt32();                        // LoopEnd
        var subInfoSize = r.ReadUInt32();
        var flags = r.ReadUInt32();

        if (format != AudioFormatOggVorbis) return null;

        long subInfoStartPos = firstAudioOffset + AudioBasicDescSize;

        // Optional marker chunk precedes the ogg seek-table header.
        if ((flags & MarkerChunkFlag) != 0)
        {
            r.ReadUInt32();                    // chunk id
            var chunkSize = r.ReadUInt32();    // chunk size (from subInfoStartPos)
            ms.Position = subInfoStartPos + chunkSize;
        }

        // OggVorbisSeekTableHeader
        var version = r.ReadByte();
        r.ReadByte();                          // StructSize
        var xorByte = r.ReadByte();
        r.ReadBytes(9);                        // Unknown[9]
        r.ReadSingle();                        // Step
        r.ReadUInt32();                        // SeekTableSize
        var oggHeaderSize = r.ReadUInt32();
        // (remaining padding of the 32-byte header is skipped by absolute seek below)
        _ = OggSeekHeaderSize;

        // Corrected position: the buggy Lumina omits "- oggHeaderSize".
        long dataPos = subInfoStartPos + subInfoSize - oggHeaderSize;
        if (dataPos < 0 || dataPos + oggHeaderSize + size > scd.Length)
            return null;

        var ogg = new byte[oggHeaderSize + size];
        ms.Position = dataPos;
        if (r.Read(ogg, 0, (int)oggHeaderSize) != (int)oggHeaderSize) return null;
        if (r.Read(ogg, (int)oggHeaderSize, (int)size) != (int)size) return null;

        switch (version)
        {
            case 2:
                for (var j = 0; j < oggHeaderSize; j++)
                    ogg[j] ^= xorByte;
                break;

            case 3:
                var byte1 = (byte)(size & 0x7F);
                var byte2 = (byte)(byte1 & 0x3F);
                for (var j = 0; j < ogg.Length; j++)
                {
                    byte x = OggXorTable[(byte2 + j) & 0xFF];
                    x ^= ogg[j];
                    x ^= byte1;
                    ogg[j] = x;
                }
                break;
        }

        return ogg;
    }

    /// <summary>
    /// Returns audio entry 0 as a standard .hca file image (the stored HCA header followed
    /// by descrambled frames), or null if the entry is not SCD format 26.
    ///
    /// A few music files (Gridania's town themes) store CRI HCA instead of Ogg Vorbis. The
    /// entry's extra data is a 0x18-byte block (byte 1 = its own size, bytes 2-3 = HCA header
    /// size) followed by the HCA header in the clear. The frames after the header are XORed
    /// with the same table and key bytes as a version 3 Ogg, with the key position counted
    /// from the start of the HCA header. Layout as parsed by vgmstream (sqex_scd.c, 0x1A).
    /// </summary>
    public static byte[]? ExtractHca(byte[] scd)
    {
        using var ms = new MemoryStream(scd, writable: false);
        using var r = new BinaryReader(ms);

        ms.Position = BinaryHeaderSize;
        r.ReadUInt16();                        // SoundCount
        r.ReadUInt16();                        // TrackCount
        var audioCount = r.ReadUInt16();       // AudioCount
        r.ReadUInt16();                        // Number
        r.ReadUInt32();                        // TrackOffset
        var audioOffset = r.ReadUInt32();      // AudioOffset

        if (audioCount == 0) return null;

        ms.Position = audioOffset;
        var firstAudioOffset = r.ReadUInt32();

        // AudioBasicDesc. LoopStart/LoopEnd here are HCA block indices; the HCA header's own
        // loop chunk carries the same blocks plus sample-exact trims, so playback reads that.
        ms.Position = firstAudioOffset;
        var size = r.ReadUInt32();
        r.ReadUInt32();                        // Channel
        r.ReadUInt32();                        // Rate
        var format = (int)r.ReadUInt32();      // Format
        r.ReadUInt32();                        // LoopStart
        r.ReadUInt32();                        // LoopEnd
        r.ReadUInt32();                        // SubInfoSize
        var flags = r.ReadUInt32();

        if (format != AudioFormatHca) return null;

        long extraPos = firstAudioOffset + AudioBasicDescSize;
        if ((flags & MarkerChunkFlag) != 0)
        {
            r.ReadUInt32();                    // chunk id
            extraPos += r.ReadUInt32();        // chunk size
            ms.Position = extraPos;
        }

        r.ReadByte();                          // Version
        var extraSize = r.ReadByte();          // size of this block; the HCA header follows it
        var headerSize = r.ReadUInt16();

        long hcaPos = extraPos + extraSize;
        if (headerSize < 8 || hcaPos + headerSize + size > scd.Length) return null;

        // "HCA\0", tolerating the high-bit masking CRI applies to chunk names.
        if ((scd[hcaPos] & 0x7F) != 'H' || (scd[hcaPos + 1] & 0x7F) != 'C' || (scd[hcaPos + 2] & 0x7F) != 'A')
            return null;

        var hca = new byte[headerSize + size];
        Array.Copy(scd, hcaPos, hca, 0, hca.Length);

        XorHcaFrames(hca.AsSpan(headerSize), headerSize);
        return hca;
    }

    /// <summary>
    /// XORs HCA frame data in place. <paramref name="frames"/> must be the entry's whole
    /// stream, because its length picks the key. The same call scrambles and descrambles.
    /// </summary>
    internal static void XorHcaFrames(Span<byte> frames, int headerSize)
    {
        var xor = (byte)(frames.Length & 0x7F);
        var keyStart = (frames.Length & 0x3F) + headerSize;
        for (var j = 0; j < frames.Length; j++)
            frames[j] ^= (byte)(OggXorTable[(keyStart + j) & 0xFF] ^ xor);
    }

    /// <summary>
    /// Returns the playable stream of audio entry 0 with its codec, or null when the entry
    /// is empty or in a format the plugin cannot decode.
    /// </summary>
    public static ScdAudio? ExtractAudio(byte[] scd)
    {
        if (Extract(scd) is { } ogg) return new ScdAudio(ScdAudioFormat.OggVorbis, ogg);
        if (ExtractHca(scd) is { } hca) return new ScdAudio(ScdAudioFormat.Hca, hca);
        return null;
    }
}

/// <summary>Codecs the plugin can decode out of an .scd music file.</summary>
public enum ScdAudioFormat
{
    OggVorbis,
    Hca,
}

/// <summary>One extracted, descrambled audio stream: Ogg Vorbis bytes or a .hca file image.</summary>
public sealed record ScdAudio(ScdAudioFormat Format, byte[] Data);
