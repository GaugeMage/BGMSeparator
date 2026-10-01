using System;
using System.Linq;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Keys;
using FFXIVClientStructs.FFXIV.Client.UI;
using GameFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace BgmSeparator.Input;

internal sealed class HotkeyController(Configuration config)
{
    private static readonly int[] KeyboardKeys = Enum.GetValues<VirtualKey>()
        .Select(key => (int)key).Where(key => key is >= 8 and < 255).Distinct().ToArray();
    private readonly bool[] _keys = new bool[256];
    public HotkeyProcessor Processor { get; } = new(config);
    public bool UiWantsTextInput { get; set; }
    public bool UiCapturesKeyboard { get; set; }

    // Read physical key-down state: the game's buffer can be cleared by Dalamud while
    // our binding button has keyboard focus, which would prevent capturing a shortcut.
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    public unsafe HotkeyAction? Update()
    {
        if (Processor.Capturing == null && config.ToggleHotkey.Key == 0
            && config.OnHotkey.Key == 0 && config.OffHotkey.Key == 0) return null;

        var framework = GameFramework.Instance();
        var atk = RaptureAtkModule.Instance();
        var blocked = framework == null || framework->WindowInactive || atk == null
            || atk->IsTextInputActive() || UiWantsTextInput;
        if (blocked)
            Array.Clear(_keys);
        else
            foreach (var key in KeyboardKeys) _keys[key] = (GetAsyncKeyState(key) & 0x8000) != 0;
        return Processor.Update(_keys, blocked, UiCapturesKeyboard);
    }

    public static string Display(HotkeyBinding binding) => binding.Key == 0 ? "Not set"
        : (binding.Ctrl ? "Ctrl + " : "") + (binding.Alt ? "Alt + " : "")
          + (binding.Shift ? "Shift + " : "") + ((VirtualKey)binding.Key).GetFancyName();
}
