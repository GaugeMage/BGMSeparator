using System;

namespace BgmSeparator.Input;

/// <summary>Keyboard capture and edge detection, independent of the game input API.</summary>
internal sealed class HotkeyProcessor(Configuration config)
{
    private static readonly HotkeyAction[] ActionPriority = [HotkeyAction.Off, HotkeyAction.On, HotkeyAction.Toggle];
    private readonly bool[] _previous = new bool[256];
    private bool _ready;
    public HotkeyAction? Capturing { get; private set; }
    public string? CaptureError { get; private set; }

    public HotkeyBinding GetBinding(HotkeyAction action) => action switch
    {
        HotkeyAction.Toggle => config.ToggleHotkey,
        HotkeyAction.On => config.OnHotkey,
        _ => config.OffHotkey,
    };

    public void BeginCapture(HotkeyAction action)
    {
        Capturing = action;
        CaptureError = null;
        _ready = false; // Release the key used to activate the Set button first.
    }

    public void CancelCapture()
    {
        Capturing = null;
        CaptureError = null;
        _ready = false;
    }

    public bool SetBinding(HotkeyAction action, HotkeyBinding binding)
    {
        foreach (var other in Enum.GetValues<HotkeyAction>())
        {
            if (other != action && binding.Key != 0 && binding.SameAs(GetBinding(other)))
            {
                CaptureError = $"That shortcut is already assigned to {other}. Choose another combination.";
                return false;
            }
        }
        switch (action)
        {
            case HotkeyAction.Toggle: config.ToggleHotkey = binding; break;
            case HotkeyAction.On: config.OnHotkey = binding; break;
            case HotkeyAction.Off: config.OffHotkey = binding; break;
        }
        config.Save();
        CancelCapture();
        return true;
    }

    public static bool IsBindable(int key) => key is >= 8 and < 255
        and not (16 or 17 or 18 or 27 or 91 or 92 or 160 or 161 or 162 or 163 or 164 or 165);

    public HotkeyAction? Update(ReadOnlySpan<bool> keys, bool blocked, bool uiCapturesKeyboard)
    {
        if (blocked || keys[91] || keys[92])
        {
            CancelCapture();
            keys.CopyTo(_previous);
            return null;
        }

        if (Capturing == null && uiCapturesKeyboard)
        {
            _ready = false;
            keys.CopyTo(_previous);
            return null;
        }

        if (!_ready)
        {
            _ready = !keys[27];
            for (var key = 8; key < 255; key++)
                if (IsBindable(key) && keys[key]) _ready = false;
            keys.CopyTo(_previous);
            return null;
        }

        HotkeyAction? result = null;
        if (Capturing is { } capture)
        {
            if (keys[27]) CancelCapture();
            else
            {
                for (var key = 8; key < 255; key++)
                {
                    if (!IsBindable(key) || !keys[key] || _previous[key]) continue;
                    SetBinding(capture, new HotkeyBinding
                    {
                        Key = key, Ctrl = keys[17], Alt = keys[18], Shift = keys[16],
                    });
                    break;
                }
            }
        }
        else
        {
            // One action per frame. Off wins if different shortcuts are pressed together.
            foreach (var action in ActionPriority)
            {
                var binding = GetBinding(action);
                if (IsBindable(binding.Key) && keys[binding.Key] && !_previous[binding.Key]
                    && binding.Ctrl == keys[17] && binding.Alt == keys[18] && binding.Shift == keys[16])
                {
                    result = action;
                    break;
                }
            }
        }

        keys.CopyTo(_previous);
        return result;
    }
}
