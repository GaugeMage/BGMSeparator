using System;

namespace BgmSeparator.Input;

[Serializable]
public sealed class HotkeyBinding
{
    // Windows virtual-key code; zero means unbound. Left/right modifiers are equivalent.
    public int Key { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }

    public bool SameAs(HotkeyBinding other) => Key == other.Key && Ctrl == other.Ctrl
        && Alt == other.Alt && Shift == other.Shift;
}

internal enum HotkeyAction { Toggle, On, Off }
