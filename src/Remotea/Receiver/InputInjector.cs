using Remotea.Native;
using Remotea.Protocol;

namespace Remotea.Receiver;

/// <summary>Injects input with SendInput and remembers what is held down so it can all be released at once.</summary>
internal sealed class InputInjector
{
    private readonly Dictionary<ushort, KeyMessage> _heldKeys = [];
    private readonly HashSet<MouseButton> _heldButtons = [];

    public bool AnyButtonDown => _heldButtons.Count > 0;

    public static void MoveTo(int x, int y, ScreenRect screen)
    {
        // Absolute coordinates on the virtual desktop are normalized to 0..65535.
        var nx = screen.Width > 1 ? (int)((x - screen.Left) * 65535L / (screen.Width - 1)) : 0;
        var ny = screen.Height > 1 ? (int)((y - screen.Top) * 65535L / (screen.Height - 1)) : 0;
        User32.SendInputs(Mouse(User32.MOUSEEVENTF_MOVE | User32.MOUSEEVENTF_ABSOLUTE | User32.MOUSEEVENTF_VIRTUALDESK, 0, nx, ny));
    }

    public void Button(MouseButton button, bool down)
    {
        if (down ? !_heldButtons.Add(button) : !_heldButtons.Remove(button))
        {
            // Ignore duplicate presses and releases of buttons we never pressed.
            return;
        }

        var (flags, data) = button switch
        {
            MouseButton.Left => (down ? User32.MOUSEEVENTF_LEFTDOWN : User32.MOUSEEVENTF_LEFTUP, 0u),
            MouseButton.Right => (down ? User32.MOUSEEVENTF_RIGHTDOWN : User32.MOUSEEVENTF_RIGHTUP, 0u),
            MouseButton.Middle => (down ? User32.MOUSEEVENTF_MIDDLEDOWN : User32.MOUSEEVENTF_MIDDLEUP, 0u),
            MouseButton.X1 => (down ? User32.MOUSEEVENTF_XDOWN : User32.MOUSEEVENTF_XUP, User32.XBUTTON1),
            _ => (down ? User32.MOUSEEVENTF_XDOWN : User32.MOUSEEVENTF_XUP, User32.XBUTTON2),
        };
        User32.SendInputs(Mouse(flags, data));
    }

    public static void Wheel(short delta, bool horizontal) =>
        User32.SendInputs(Mouse(horizontal ? User32.MOUSEEVENTF_HWHEEL : User32.MOUSEEVENTF_WHEEL, unchecked((uint)delta)));

    public void Key(KeyMessage key)
    {
        if (KeyTranslation.Translate(key.VirtualKey, key.ScanCode, key.IsExtended) is not { } injection)
        {
            return;
        }

        if (key.IsUp)
        {
            _heldKeys.Remove(key.VirtualKey);
        }
        else
        {
            _heldKeys[key.VirtualKey] = key;
        }

        User32.SendInputs(Keyboard(injection, key.IsUp));
    }

    public void ReleaseAll()
    {
        foreach (var key in _heldKeys.Values.ToList())
        {
            Key(key with { Flags = key.Flags | KeyFlags.Up });
        }

        foreach (var button in _heldButtons.ToList())
        {
            Button(button, down: false);
        }
    }

    private static User32.INPUT Mouse(uint flags, uint data, int dx = 0, int dy = 0) => new()
    {
        type = User32.INPUT_MOUSE,
        u = new User32.InputUnion { mi = new User32.MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags } },
    };

    private static User32.INPUT Keyboard(KeyInjection key, bool up)
    {
        var flags = (up ? User32.KEYEVENTF_KEYUP : 0) | (key.Extended ? User32.KEYEVENTF_EXTENDEDKEY : 0);
        var input = key.UseScanCode
            ? new User32.KEYBDINPUT { wScan = key.ScanCode, dwFlags = flags | User32.KEYEVENTF_SCANCODE }
            : new User32.KEYBDINPUT { wVk = key.VirtualKey, wScan = key.ScanCode, dwFlags = flags };
        return new User32.INPUT { type = User32.INPUT_KEYBOARD, u = new User32.InputUnion { ki = input } };
    }
}
