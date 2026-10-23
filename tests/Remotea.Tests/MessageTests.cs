using Remotea.Protocol;

namespace Remotea.Tests;

public class MessageTests
{
    private static readonly Message[] AllMessages =
    [
        new HelloMessage(SecureChannel.ProtocolVersion),
        new HeartbeatMessage(),
        new EnterMessage(ScreenEdge.Bottom, 0.25f),
        new MouseMoveMessage(-1200, 37),
        new MouseButtonMessage(MouseButton.X2, true),
        new WheelMessage(-120, true),
        new KeyMessage(0x41, 0x1E, KeyFlags.Up | KeyFlags.Extended),
        new LeaveMessage(0.75f),
        new ReleaseAllMessage(),
    ];

    [Fact]
    public void EveryTypeRoundTrips()
    {
        Assert.Equal(Enum.GetValues<MessageType>().Length, AllMessages.Select(m => m.Type).Distinct().Count());
        foreach (var message in AllMessages)
        {
            Assert.Equal(message, Message.Parse(message.ToBytes()));
        }
    }

    [Fact]
    public void RejectsUnknownType()
    {
        Assert.Throws<InvalidDataException>(() => Message.Parse([0xEE]));
    }

    [Fact]
    public void RejectsTruncatedPayload()
    {
        var bytes = new MouseMoveMessage(1, 2).ToBytes();
        Assert.Throws<InvalidDataException>(() => Message.Parse(bytes.AsSpan(0, 3)));
    }
}
