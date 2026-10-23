using System.Threading.Channels;

namespace Remotea.Protocol;

/// <summary>Runs one connected session: drains an outbox, dispatches incoming messages and keeps heartbeats flowing.</summary>
internal static class Session
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(6);

    public static Channel<Message> CreateOutbox() =>
        Channel.CreateUnbounded<Message>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Returns or throws when the connection ends; <paramref name="onMessage"/> never sees heartbeats.</summary>
    public static async Task RunAsync(SecureChannel channel, ChannelReader<Message> outbox, Action<Message> onMessage, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var lastReceived = Environment.TickCount64;

        async Task SendLoop()
        {
            await foreach (var message in outbox.ReadAllAsync(cts.Token))
            {
                await channel.SendAsync(message, cts.Token);
            }
        }

        async Task ReceiveLoop()
        {
            while (true)
            {
                var message = await channel.ReceiveAsync(cts.Token);
                Interlocked.Exchange(ref lastReceived, Environment.TickCount64);
                if (message is not HeartbeatMessage)
                {
                    onMessage(message);
                }
            }
        }

        async Task HeartbeatLoop()
        {
            using var timer = new PeriodicTimer(HeartbeatInterval);
            while (await timer.WaitForNextTickAsync(cts.Token))
            {
                if (Environment.TickCount64 - Interlocked.Read(ref lastReceived) > PeerTimeout.TotalMilliseconds)
                {
                    throw new TimeoutException("Peer stopped responding.");
                }

                await channel.SendAsync(new HeartbeatMessage(), cts.Token);
            }
        }

        Task[] loops = [SendLoop(), ReceiveLoop(), HeartbeatLoop()];
        var first = await Task.WhenAny(loops);
        cts.Cancel();
        try
        {
            await Task.WhenAll(loops);
        }
        catch
        {
            // The first loop to finish carries the reason; the rest just observed cancellation.
        }

        await first;
    }
}
