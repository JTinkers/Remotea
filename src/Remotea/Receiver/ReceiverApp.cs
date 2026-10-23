using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Remotea.Native;
using Remotea.Protocol;

namespace Remotea.Receiver;

/// <summary>
/// Listens for one controller at a time and replays its input. While controlled, the cursor position is tracked
/// here; pushing it out through the edge it entered from hands control back to the controller.
/// </summary>
internal sealed class ReceiverApp(Config config, byte[] masterKey)
{
    private readonly object _gate = new();
    private readonly InputInjector _injector = new();

    // Guarded by _gate.
    private object? _currentSession;
    private CancellationTokenSource? _currentCts;
    private Channel<Message>? _outbox;
    private bool _active;
    private ScreenEdge _returnEdge;
    private int _x;
    private int _y;

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, config.Port);
        listener.Start();
        Log.Info($"Receiver listening on 0.0.0.0:{config.Port}.");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            listener.Stop();
            lock (_gate)
            {
                _injector.ReleaseAll();
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var remote = client.Client.RemoteEndPoint;
        var session = new object();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            client.NoDelay = true;
            await using var channel = await SecureChannel.AcceptAsServerAsync(client.GetStream(), masterKey, cts.Token)
                .WaitAsync(TimeSpan.FromSeconds(10), cts.Token);
            var outbox = Session.CreateOutbox();
            lock (_gate)
            {
                // A new controller replaces the old one.
                _currentCts?.Cancel();
                _currentSession = session;
                _currentCts = cts;
                _outbox = outbox;
            }

            Log.Info($"Controller {remote} connected.");
            await Session.RunAsync(channel, outbox.Reader, m => OnMessage(session, m), cts.Token);
        }
        catch (AuthenticationFailedException)
        {
            Log.Warn($"Rejected {remote}: authentication failed (wrong secret).");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Log.Warn($"Controller {remote} disconnected: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            client.Dispose();
            lock (_gate)
            {
                if (_currentSession == session)
                {
                    _currentSession = null;
                    _currentCts = null;
                    _outbox = null;
                    _active = false;
                    _injector.ReleaseAll();
                }
            }
        }
    }

    private void OnMessage(object session, Message message)
    {
        lock (_gate)
        {
            if (_currentSession != session)
            {
                return;
            }

            switch (message)
            {
                case EnterMessage enter:
                    var screen = User32.GetVirtualScreen();
                    _returnEdge = Edge.Opposite(enter.Edge);
                    (_x, _y) = Edge.PointInside(screen, _returnEdge, enter.Ratio);
                    User32.SetCursorPos(_x, _y);
                    _active = true;
                    Log.Info("Controller took control.");
                    break;
                case MouseMoveMessage move when _active:
                    OnMouseMove(move.Dx, move.Dy);
                    break;
                case MouseButtonMessage button:
                    _injector.Button(button.Button, button.Down);
                    break;
                case WheelMessage wheel:
                    InputInjector.Wheel(wheel.Delta, wheel.Horizontal);
                    break;
                case KeyMessage key:
                    Log.Debug($"Key vk=0x{key.VirtualKey:X2} scan=0x{key.ScanCode:X2} flags={key.Flags}");
                    _injector.Key(key);
                    break;
                case ReleaseAllMessage:
                    _active = false;
                    _injector.ReleaseAll();
                    Log.Info("Controller released control (hotkey).");
                    break;
            }
        }
    }

    private void OnMouseMove(int dx, int dy)
    {
        var screen = User32.GetVirtualScreen();
        (_x, _y) = screen.Clamp(_x + dx, _y + dy);
        if (Edge.IsAt(_x, _y, screen, _returnEdge) && Edge.IsPushingOut(dx, dy, _returnEdge) && !_injector.AnyButtonDown)
        {
            _active = false;
            _injector.ReleaseAll();
            _outbox?.Writer.TryWrite(new LeaveMessage(Edge.RatioAlong(_x, _y, screen, _returnEdge)));
            Log.Info("Control handed back to controller.");
            return;
        }

        InputInjector.MoveTo(_x, _y, screen);
    }
}
