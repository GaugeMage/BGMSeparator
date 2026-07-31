using System;
using LuminaBgm = Lumina.Excel.Sheets.BGM;

namespace BgmSeparator.Audio;

/// <summary>
/// Maps a BGM sheet row id to the game's music file and returns the decoded
/// (already XOR-descrambled) Ogg Vorbis bytes for playback.
/// </summary>
public static class ScdBgmLoader
{
    /// <summary>Resolves the .scd path for a BGM row id, or null if it has none.</summary>
    public static string? GetScdPath(int songId)
    {
        var sheet = Services.DataManager.GetExcelSheet<LuminaBgm>();
        if (sheet is null) return null;

        var row = sheet.GetRowOrDefault((uint)songId);
        if (row is null) return null;

        // BGM.File points at e.g. "music/ffxiv/BGM_System_Title.scd"
        var path = row.Value.File.ExtractText();
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    /// <summary>
    /// Returns the descrambled Ogg Vorbis bytes for a BGM row id, or null if it cannot
    /// be resolved / is not an Ogg stream. We read the raw .scd and extract it ourselves
    /// (see <see cref="ScdOggExtractor"/>) to avoid the buggy bundled Lumina GetAudio().
    /// </summary>
    public static byte[]? LoadOgg(int songId)
    {
        try
        {
            var path = GetScdPath(songId);
            if (path is null) return null;

            var file = Services.DataManager.GetFile(path);
            if (file is null || file.Data is null || file.Data.Length < 64) return null;

            var data = ScdOggExtractor.Extract(file.Data);
            if (data is null || data.Length < 4) return null;

            // Sanity check: valid entries start with the "OggS" page signature.
            if (!(data[0] == (byte)'O' && data[1] == (byte)'g' && data[2] == (byte)'g' && data[3] == (byte)'S'))
            {
                Services.Log.Warning($"[BgmSeparator] BGM {songId} ({path}) did not decode to Ogg Vorbis; skipping.");
                return null;
            }

            return data;
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, $"[BgmSeparator] Failed to load BGM {songId}");
            return null;
        }
    }
}
