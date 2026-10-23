using Remotea.Receiver;

namespace Remotea.Tests;

// Inputs are what the controller's low-level hook actually reported (from a receiver --verbose log).
public class KeyTranslationTests
{
    [Fact]
    public void RightShiftDropsBogusExtendedFlag()
    {
        Assert.Equal(new KeyInjection(0xA1, 0x36, false, true), KeyTranslation.Translate(0xA1, 0x36, extended: true));
    }

    [Fact]
    public void LeftShiftAndLettersUseScanCodes()
    {
        Assert.Equal(new KeyInjection(0xA0, 0x2A, false, true), KeyTranslation.Translate(0xA0, 0x2A, extended: false));
        Assert.Equal(new KeyInjection(0x31, 0x02, false, true), KeyTranslation.Translate(0x31, 0x02, extended: false));
    }

    [Fact]
    public void AltGrFakeCtrlIsDroppedButRealCtrlAndRightAltAreKept()
    {
        Assert.Null(KeyTranslation.Translate(0xA2, 0x21D, extended: false));
        Assert.Equal(new KeyInjection(0xA2, 0x1D, false, true), KeyTranslation.Translate(0xA2, 0x1D, extended: false));
        Assert.Equal(new KeyInjection(0xA5, 0x38, true, true), KeyTranslation.Translate(0xA5, 0x38, extended: true));
    }

    [Fact]
    public void ExtendedNavigationKeysKeepFlag()
    {
        Assert.Equal(new KeyInjection(0x25, 0x4B, true, true), KeyTranslation.Translate(0x25, 0x4B, extended: true));
    }

    [Theory]
    [InlineData(0x13, 0x45, false)] // Pause
    [InlineData(0x90, 0x45, true)] // NumLock
    [InlineData(0x2C, 0x37, true)] // PrintScreen
    [InlineData(0xAF, 0x30, true)] // Volume up
    [InlineData(0xB3, 0x22, true)] // Play/Pause
    [InlineData(0x41, 0x00, false)] // No scan code
    public void AmbiguousKeysUseVirtualKey(int vk, int scan, bool extended)
    {
        var injection = KeyTranslation.Translate((ushort)vk, (ushort)scan, extended);

        Assert.NotNull(injection);
        Assert.False(injection.Value.UseScanCode);
        Assert.Equal((ushort)vk, injection.Value.VirtualKey);
    }
}
