using System.Runtime.InteropServices;

namespace BgmSeparator.Bgm;

/// <summary>
/// Layout of a single entry in the game's BGM scene list.
/// Copied field-for-field from Orchestrion (perchbirdd/OrchestrionPlugin) so the
/// struct stride matches the game's array; we only ever read <see cref="BgmId"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct BgmScene
{
    public int SceneIndex;
    public int Flags;
    private int Padding1;
    public ushort BgmReference; // Reference into BGM/BGMSwitch/BGMSituation sheets
    public ushort BgmId;        // The BGM row actually playing right now
    public ushort PreviousBgmId;
    public byte TimerEnable;
    private byte Padding2;
    public float Timer;
    private fixed byte DisableRestartList[24];
    private byte Unknown1;
    private uint Unknown2;
    private uint Unknown3;
    private uint Unknown4;
    private uint Unknown5;
    private uint Unknown6;
    private ulong Unknown7;
    private uint Unknown8;
    private byte Unknown9;
    private byte Unknown10;
    private byte Unknown11;
    private byte Unknown12;
    private float Unknown13;
    private uint Unknown14;
}
