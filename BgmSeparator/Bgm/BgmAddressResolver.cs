using System;
using System.Runtime.InteropServices;

namespace BgmSeparator.Bgm;

/// <summary>
/// Resolves the address of the game's BGM scene list via signature scanning.
/// Signature taken from Orchestrion (perchbirdd/OrchestrionPlugin).
/// </summary>
public static class BgmAddressResolver
{
    private static nint _baseAddress;

    public static void Init()
    {
        _baseAddress = Services.SigScanner.GetStaticAddressFromSig(
            "48 8B 05 ?? ?? ?? ?? 48 85 C0 74 51 83 78 08 0B");
        Services.Log.Debug($"[BgmSeparator] BGM base address: {_baseAddress.ToInt64():X}");
    }

    /// <summary>Pointer to the contiguous array of 12 <see cref="BgmScene"/> structs, or Zero.</summary>
    public static nint BgmSceneList
    {
        get
        {
            if (_baseAddress == nint.Zero) return nint.Zero;
            var baseObject = Marshal.ReadIntPtr(_baseAddress);
            return baseObject == nint.Zero ? nint.Zero : Marshal.ReadIntPtr(baseObject + 0xC0);
        }
    }
}
