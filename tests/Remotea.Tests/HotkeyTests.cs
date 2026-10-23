using Remotea.Controller;

namespace Remotea.Tests;

public class HotkeyTests
{
    private const ushort LeftCtrl = 0xA2;
    private const ushort RightAlt = 0xA5;
    private const ushort F12 = 0x7B;

    [Fact]
    public void ParsesModifiersAndKey()
    {
        Assert.Equal(new Hotkey(Modifiers.Ctrl | Modifiers.Alt, F12), Hotkey.Parse("Ctrl+Alt+F12"));
        Assert.Equal(new Hotkey(Modifiers.Shift, 0x91), Hotkey.Parse("shift + ScrollLock"));
        Assert.Equal(new Hotkey(Modifiers.Win, '7'), Hotkey.Parse("Win+7"));
    }

    [Theory]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+F1+F2")]
    [InlineData("Ctrl+Nope")]
    public void RejectsInvalid(string text)
    {
        Assert.Throws<FormatException>(() => Hotkey.Parse(text));
    }

    [Fact]
    public void MatchesWithEitherModifierSide()
    {
        var hotkey = Hotkey.Parse("Ctrl+Alt+F12");

        Assert.True(hotkey.Matches(F12, new HashSet<ushort> { LeftCtrl, RightAlt, F12 }));
        Assert.False(hotkey.Matches(F12, new HashSet<ushort> { LeftCtrl, F12 }));
        Assert.False(hotkey.Matches(0x7A, new HashSet<ushort> { LeftCtrl, RightAlt }));
    }
}
