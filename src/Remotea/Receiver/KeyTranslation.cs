namespace Remotea.Receiver;

/// <summary>How to replay a captured key with SendInput.</summary>
internal readonly record struct KeyInjection(ushort VirtualKey, ushort ScanCode, bool Extended, bool UseScanCode);

/// <summary>
/// Turns what the controller's low-level hook reported into something SendInput replays faithfully.
/// Scan codes are preferred so the receiver's own layout picks the character; a few keys only work by virtual key.
/// </summary>
internal static class KeyTranslation
{
    private const ushort VkCancel = 0x03;
    private const ushort VkPause = 0x13;
    private const ushort VkSnapshot = 0x2C;
    private const ushort VkSleep = 0x5F;
    private const ushort VkNumLock = 0x90;
    private const ushort VkRShift = 0xA1;
    private const ushort VkBrowserBack = 0xA6;
    private const ushort VkLaunchApp2 = 0xB7;

    /// <summary>Returns null for events that must not be replayed.</summary>
    public static KeyInjection? Translate(ushort virtualKey, ushort scanCode, bool extended)
    {
        // AltGr layouts (Polish, Czech, German...) make Windows emit a fake LCtrl with scan 0x21D before RAlt.
        // The receiver's layout synthesizes it again from the replayed RAlt, so replaying it would double it.
        if (scanCode > 0xFF)
        {
            return null;
        }

        // The hook flags right Shift as extended, but E0 36 isn't a key; plain 36 is right Shift.
        if (virtualKey == VkRShift)
        {
            extended = false;
        }

        // Keys with ambiguous or multi-byte scan codes (Pause/NumLock share 45, PrintScreen, Ctrl+Break)
        // and HID consumer keys (media, volume, browser, launch) are only reliable by virtual key.
        var byVirtualKey = scanCode == 0
            || virtualKey is VkCancel or VkPause or VkSnapshot or VkSleep or VkNumLock
            || virtualKey is >= VkBrowserBack and <= VkLaunchApp2;

        return new KeyInjection(virtualKey, scanCode, extended, UseScanCode: !byVirtualKey);
    }
}
