using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Remotea.Protocol;

/// <summary>
/// Authenticated, encrypted message framing over a stream.
/// Handshake: client sends 16 random bytes, server replies with 16; both derive a session key from the
/// shared master key and the two nonces. Frames are AES-GCM with an implicit per-direction counter as nonce,
/// so a wrong secret, tampering, replay or reordering all surface as a tag mismatch.
/// </summary>
internal sealed class SecureChannel : IAsyncDisposable
{
    public const ushort ProtocolVersion = 1;

    private const int HandshakeNonceSize = 16;
    private const int TagSize = 16;
    private const int MaxFrameSize = ushort.MaxValue;
    private const int Pbkdf2Iterations = 200_000;
    private static readonly byte[] MasterKeySalt = Encoding.ASCII.GetBytes("Remotea.v1");
    private static readonly byte[] SessionKeyInfo = Encoding.ASCII.GetBytes("session");

    private readonly Stream _stream;
    private readonly AesGcm _aes;
    private readonly byte _sendDirection;
    private readonly byte _receiveDirection;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ulong _sendCounter;
    private ulong _receiveCounter;

    private SecureChannel(Stream stream, byte[] sessionKey, bool isClient)
    {
        _stream = stream;
        _aes = new AesGcm(sessionKey, TagSize);
        _sendDirection = isClient ? (byte)0 : (byte)1;
        _receiveDirection = isClient ? (byte)1 : (byte)0;
    }

    public static byte[] DeriveMasterKey(string secret) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), MasterKeySalt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);

    public static async Task<SecureChannel> ConnectAsClientAsync(Stream stream, byte[] masterKey, CancellationToken ct)
    {
        var clientNonce = RandomNumberGenerator.GetBytes(HandshakeNonceSize);
        await stream.WriteAsync(clientNonce, ct);
        var serverNonce = new byte[HandshakeNonceSize];
        await stream.ReadExactlyAsync(serverNonce, ct);

        var channel = new SecureChannel(stream, DeriveSessionKey(masterKey, clientNonce, serverNonce), isClient: true);
        await channel.SendAsync(new HelloMessage(ProtocolVersion), ct);
        await channel.ExpectHelloAsync(ct);
        return channel;
    }

    public static async Task<SecureChannel> AcceptAsServerAsync(Stream stream, byte[] masterKey, CancellationToken ct)
    {
        var clientNonce = new byte[HandshakeNonceSize];
        await stream.ReadExactlyAsync(clientNonce, ct);
        var serverNonce = RandomNumberGenerator.GetBytes(HandshakeNonceSize);
        await stream.WriteAsync(serverNonce, ct);

        var channel = new SecureChannel(stream, DeriveSessionKey(masterKey, clientNonce, serverNonce), isClient: false);
        await channel.ExpectHelloAsync(ct);
        await channel.SendAsync(new HelloMessage(ProtocolVersion), ct);
        return channel;
    }

    public async Task SendAsync(Message message, CancellationToken ct)
    {
        var plaintext = message.ToBytes();
        var frame = new byte[2 + plaintext.Length + TagSize];
        BinaryPrimitives.WriteUInt16BigEndian(frame, (ushort)(plaintext.Length + TagSize));

        await _sendLock.WaitAsync(ct);
        try
        {
            var nonce = BuildNonce(_sendDirection, _sendCounter++);
            _aes.Encrypt(nonce, plaintext, frame.AsSpan(2, plaintext.Length), frame.AsSpan(2 + plaintext.Length, TagSize));
            await _stream.WriteAsync(frame, ct);
            await _stream.FlushAsync(ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Reads the next message. Not thread-safe: use a single reader loop.</summary>
    public async Task<Message> ReceiveAsync(CancellationToken ct)
    {
        var header = new byte[2];
        await _stream.ReadExactlyAsync(header, ct);
        int length = BinaryPrimitives.ReadUInt16BigEndian(header);
        if (length <= TagSize || length > MaxFrameSize)
        {
            throw new InvalidDataException($"Invalid frame length {length}.");
        }

        var body = new byte[length];
        await _stream.ReadExactlyAsync(body, ct);

        var plaintext = new byte[length - TagSize];
        var nonce = BuildNonce(_receiveDirection, _receiveCounter++);
        try
        {
            _aes.Decrypt(nonce, body.AsSpan(0, plaintext.Length), body.AsSpan(plaintext.Length), plaintext);
        }
        catch (CryptographicException ex)
        {
            throw new AuthenticationFailedException("Frame authentication failed (wrong secret or tampered data).", ex);
        }

        return Message.Parse(plaintext);
    }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync();
        _aes.Dispose();
        _sendLock.Dispose();
    }

    private async Task ExpectHelloAsync(CancellationToken ct)
    {
        var message = await ReceiveAsync(ct);
        if (message is not HelloMessage hello)
        {
            throw new InvalidDataException($"Expected Hello, got {message.Type}.");
        }

        if (hello.Version != ProtocolVersion)
        {
            throw new InvalidDataException($"Protocol version mismatch: peer {hello.Version}, local {ProtocolVersion}.");
        }
    }

    private static byte[] DeriveSessionKey(byte[] masterKey, byte[] clientNonce, byte[] serverNonce) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, [.. clientNonce, .. serverNonce], SessionKeyInfo);

    private static byte[] BuildNonce(byte direction, ulong counter)
    {
        var nonce = new byte[12];
        nonce[0] = direction;
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), counter);
        return nonce;
    }
}

internal sealed class AuthenticationFailedException(string message, Exception inner) : Exception(message, inner);
