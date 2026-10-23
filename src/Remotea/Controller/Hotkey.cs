namespace Remotea.Controller;

[Flags]
internal enum Modifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A key plus required modifiers, e.g. "Ctrl+Alt+F12". Either left or right modifier variant satisfies it.</summary>
internal sealed record Hotkey(Modifiers Modifiers, ushort VirtualKey)
{
    private const ushort VkScrollLock = 0x91;

    private static readonly (Modifiers Modifier, ushort Left, ushort Right)[] ModifierKeys =
    [
        (Modifiers.Shift, 0xA0, 0xA1),
        (Modifiers.Ctrl, 0xA2, 0xA3),
        (Modifiers.Alt, 0xA4, 0xA5),
        (Modifiers.Win, 0x5B, 0x5C),
    ];

    public static Hotkey Parse(string text)
    {
        var modifiers = Modifiers.None;
        ushort? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= Modifiers.Ctrl;
                    break;
                case "alt":
                    modifiers |= Modifiers.Alt;
                    break;
                case "shift":
                    modifiers |= Modifiers.Shift;
                    break;
                case "win":
                    modifiers |= Modifiers.Win;
                    break;
                default:
                    key = key is null ? ParseKey(raw) : throw new FormatException($"Hotkey '{text}' has more than one non-modifier key.");
                    break;
            }
        }

        return new Hotkey(modifiers, key ?? throw new FormatException($"Hotkey '{text}' has no key."));
    }

    public bool Matches(ushort virtualKey, IReadOnlySet<ushort> heldKeys) =>
        virtualKey == VirtualKey &&
        ModifierKeys.All(m => !Modifiers.HasFlag(m.Modifier) || heldKeys.Contains(m.Left) || heldKeys.Contains(m.Right));

    private static ushort ParseKey(string name)
    {
        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
        {
            return name[0];
        }

        if (name.Equals("esc", StringComparison.OrdinalIgnoreCase))
        {
            return (ushort)ConsoleKey.Escape;
        }

        if (name.Equals("scrolllock", StringComparison.OrdinalIgnoreCase))
        {
            return VkScrollLock;
        }

        // ConsoleKey values are Windows virtual-key codes.
        return Enum.TryParse<ConsoleKey>(name, ignoreCase: true, out var consoleKey)
            ? (ushort)consoleKey
            : throw new FormatException($"Unknown hotkey key '{name}'.");
    }
}
