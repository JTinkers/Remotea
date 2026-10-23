using System.Buffers.Binary;

namespace Remotea.Protocol;

internal enum MessageType : byte
{
    Hello = 1,
    Heartbeat = 2,
    Enter = 3,
    MouseMove = 4,
    MouseButton = 5,
    Wheel = 6,
    Key = 7,
    Leave = 8,
    ReleaseAll = 9,
}

internal enum MouseButton : byte
{
    Left = 0,
    Right = 1,
    Middle = 2,
    X1 = 3,
    X2 = 4,
}

[Flags]
internal enum KeyFlags : byte
{
    None = 0,
    Up = 1,
    Extended = 2,
}

internal abstract record Message
{
    public abstract MessageType Type { get; }

    protected abstract int PayloadLength { get; }

    protected abstract void WritePayload(Span<byte> payload);

    public byte[] ToBytes()
    {
        var bytes = new byte[1 + PayloadLength];
        bytes[0] = (byte)Type;
        WritePayload(bytes.AsSpan(1));
        return bytes;
    }

    public static Message Parse(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            throw new InvalidDataException("Empty message.");
        }

        var p = data[1..];
        var type = (MessageType)data[0];
        Message message = type switch
        {
            MessageType.Hello => new HelloMessage(BinaryPrimitives.ReadUInt16LittleEndian(Need(p, 2))),
            MessageType.Heartbeat => new HeartbeatMessage(),
            MessageType.Enter => new EnterMessage((ScreenEdge)Need(p, 5)[0], BinaryPrimitives.ReadSingleLittleEndian(p[1..])),
            MessageType.MouseMove => new MouseMoveMessage(
                BinaryPrimitives.ReadInt16LittleEndian(Need(p, 4)),
                BinaryPrimitives.ReadInt16LittleEndian(p[2..])),
            MessageType.MouseButton => new MouseButtonMessage((MouseButton)Need(p, 2)[0], p[1] != 0),
            MessageType.Wheel => new WheelMessage(BinaryPrimitives.ReadInt16LittleEndian(Need(p, 3)), p[2] != 0),
            MessageType.Key => new KeyMessage(
                BinaryPrimitives.ReadUInt16LittleEndian(Need(p, 5)),
                BinaryPrimitives.ReadUInt16LittleEndian(p[2..]),
                (KeyFlags)p[4]),
            MessageType.Leave => new LeaveMessage(BinaryPrimitives.ReadSingleLittleEndian(Need(p, 4))),
            MessageType.ReleaseAll => new ReleaseAllMessage(),
            _ => throw new InvalidDataException($"Unknown message type {data[0]}."),
        };
        return message;
    }

    private static ReadOnlySpan<byte> Need(ReadOnlySpan<byte> payload, int length) =>
        payload.Length >= length ? payload : throw new InvalidDataException("Truncated message.");
}

internal sealed record HelloMessage(ushort Version) : Message
{
    public override MessageType Type => MessageType.Hello;
    protected override int PayloadLength => 2;
    protected override void WritePayload(Span<byte> payload) => BinaryPrimitives.WriteUInt16LittleEndian(payload, Version);
}

internal sealed record HeartbeatMessage : Message
{
    public override MessageType Type => MessageType.Heartbeat;
    protected override int PayloadLength => 0;
    protected override void WritePayload(Span<byte> payload) { }
}

/// <summary>Controller hands control to the receiver; <see cref="Ratio"/> is the 0..1 position along the crossed edge.</summary>
internal sealed record EnterMessage(ScreenEdge Edge, float Ratio) : Message
{
    public override MessageType Type => MessageType.Enter;
    protected override int PayloadLength => 5;

    protected override void WritePayload(Span<byte> payload)
    {
        payload[0] = (byte)Edge;
        BinaryPrimitives.WriteSingleLittleEndian(payload[1..], Ratio);
    }
}

internal sealed record MouseMoveMessage(short Dx, short Dy) : Message
{
    public override MessageType Type => MessageType.MouseMove;
    protected override int PayloadLength => 4;

    protected override void WritePayload(Span<byte> payload)
    {
        BinaryPrimitives.WriteInt16LittleEndian(payload, Dx);
        BinaryPrimitives.WriteInt16LittleEndian(payload[2..], Dy);
    }
}

internal sealed record MouseButtonMessage(MouseButton Button, bool Down) : Message
{
    public override MessageType Type => MessageType.MouseButton;
    protected override int PayloadLength => 2;

    protected override void WritePayload(Span<byte> payload)
    {
        payload[0] = (byte)Button;
        payload[1] = Down ? (byte)1 : (byte)0;
    }
}

internal sealed record WheelMessage(short Delta, bool Horizontal) : Message
{
    public override MessageType Type => MessageType.Wheel;
    protected override int PayloadLength => 3;

    protected override void WritePayload(Span<byte> payload)
    {
        BinaryPrimitives.WriteInt16LittleEndian(payload, Delta);
        payload[2] = Horizontal ? (byte)1 : (byte)0;
    }
}

internal sealed record KeyMessage(ushort VirtualKey, ushort ScanCode, KeyFlags Flags) : Message
{
    public override MessageType Type => MessageType.Key;
    protected override int PayloadLength => 5;

    public bool IsUp => Flags.HasFlag(KeyFlags.Up);

    public bool IsExtended => Flags.HasFlag(KeyFlags.Extended);

    protected override void WritePayload(Span<byte> payload)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(payload, VirtualKey);
        BinaryPrimitives.WriteUInt16LittleEndian(payload[2..], ScanCode);
        payload[4] = (byte)Flags;
    }
}

/// <summary>Receiver's cursor left through the return edge; <see cref="Ratio"/> is the 0..1 position along it.</summary>
internal sealed record LeaveMessage(float Ratio) : Message
{
    public override MessageType Type => MessageType.Leave;
    protected override int PayloadLength => 4;
    protected override void WritePayload(Span<byte> payload) => BinaryPrimitives.WriteSingleLittleEndian(payload, Ratio);
}

internal sealed record ReleaseAllMessage : Message
{
    public override MessageType Type => MessageType.ReleaseAll;
    protected override int PayloadLength => 0;
    protected override void WritePayload(Span<byte> payload) { }
}
