using System.Net;
using System.Net.Sockets;
using Remotea.Protocol;

namespace Remotea.Tests;

public class SecureChannelTests
{
    private static readonly byte[] Key = SecureChannel.DeriveMasterKey("correct horse");
    private static readonly byte[] OtherKey = SecureChannel.DeriveMasterKey("battery staple");

    [Fact]
    public async Task HandshakeAndMessagesRoundTrip()
    {
        var (clientStream, serverStream) = DuplexPair();
        var serverTask = SecureChannel.AcceptAsServerAsync(serverStream, Key, Timeout());
        await using var client = await SecureChannel.ConnectAsClientAsync(clientStream, Key, Timeout());
        await using var server = await serverTask;

        await client.SendAsync(new MouseMoveMessage(5, -3), Timeout());
        await client.SendAsync(new KeyMessage(0x41, 0x1E, KeyFlags.None), Timeout());
        await server.SendAsync(new LeaveMessage(0.5f), Timeout());

        Assert.Equal(new MouseMoveMessage(5, -3), await server.ReceiveAsync(Timeout()));
        Assert.Equal(new KeyMessage(0x41, 0x1E, KeyFlags.None), await server.ReceiveAsync(Timeout()));
        Assert.Equal(new LeaveMessage(0.5f), await client.ReceiveAsync(Timeout()));
    }

    [Fact]
    public async Task WrongSecretFailsHandshake()
    {
        var (clientStream, serverStream) = DuplexPair();
        var serverTask = SecureChannel.AcceptAsServerAsync(serverStream, OtherKey, Timeout());
        var clientTask = SecureChannel.ConnectAsClientAsync(clientStream, Key, Timeout());

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => serverTask);
    }

    [Fact]
    public async Task TamperedFrameIsRejected()
    {
        var recorder = new TamperingStream();
        var (clientStream, serverStream) = DuplexPair(clientWrapper: s => recorder.Wrap(s));
        var serverTask = SecureChannel.AcceptAsServerAsync(serverStream, Key, Timeout());
        await using var client = await SecureChannel.ConnectAsClientAsync(clientStream, Key, Timeout());
        await using var server = await serverTask;

        recorder.FlipNextWriteByte = 4;
        await client.SendAsync(new MouseMoveMessage(1, 1), Timeout());

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => server.ReceiveAsync(Timeout()));
    }

    [Fact]
    public async Task ReplayedFrameIsRejected()
    {
        var recorder = new TamperingStream();
        var (clientStream, serverStream) = DuplexPair(clientWrapper: s => recorder.Wrap(s));
        var serverTask = SecureChannel.AcceptAsServerAsync(serverStream, Key, Timeout());
        await using var client = await SecureChannel.ConnectAsClientAsync(clientStream, Key, Timeout());
        await using var server = await serverTask;

        await client.SendAsync(new MouseMoveMessage(1, 1), Timeout());
        Assert.Equal(new MouseMoveMessage(1, 1), await server.ReceiveAsync(Timeout()));

        await recorder.ReplayLastWriteAsync();
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => server.ReceiveAsync(Timeout()));
    }

    private static CancellationToken Timeout() => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    /// <summary>A connected loopback TCP pair, like the real transport.</summary>
    private static (Stream Client, Stream Server) DuplexPair(Func<Stream, Stream>? clientWrapper = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient();
        client.Connect((IPEndPoint)listener.LocalEndpoint);
        var server = listener.AcceptTcpClient();
        listener.Stop();
        Stream clientStream = client.GetStream();
        return (clientWrapper?.Invoke(clientStream) ?? clientStream, server.GetStream());
    }

    /// <summary>Wraps the client stream to corrupt or replay what the client writes.</summary>
    private sealed class TamperingStream
    {
        private Stream? _inner;
        private byte[] _lastWrite = [];

        public int? FlipNextWriteByte { get; set; }

        public Stream Wrap(Stream inner)
        {
            _inner = inner;
            return new Interceptor(this, inner);
        }

        public async Task ReplayLastWriteAsync()
        {
            await _inner!.WriteAsync(_lastWrite);
            await _inner.FlushAsync();
        }

        private sealed class Interceptor(TamperingStream owner, Stream inner) : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override void Flush() => inner.Flush();
            public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
            public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer, ct);
            public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
            {
                var bytes = buffer.ToArray();
                if (owner.FlipNextWriteByte is { } index)
                {
                    bytes[index] ^= 0xFF;
                    owner.FlipNextWriteByte = null;
                }

                owner._lastWrite = bytes;
                return inner.WriteAsync(bytes, ct);
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
