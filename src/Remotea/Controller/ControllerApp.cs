using System.Net.Sockets;
using System.Threading.Channels;
using Remotea.Native;
using Remotea.Protocol;

namespace Remotea.Controller;

/// <summary>
/// Captures local input; while the cursor is on this PC everything passes through, and once it crosses the
/// configured edge all mouse and keyboard input is swallowed and forwarded to the receiver.
/// </summary>
internal sealed class ControllerApp
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(5);

    private readonly Config _config;
    private readonly byte[] _masterKey;
    private readonly Hotkey _releaseHotkey;
    private readonly object _gate = new();

    // All state below is guarded by _gate; hook callbacks and the network loop both touch it.
    private Channel<Message>? _outbox;
    private bool _remote;
    private readonly HashSet<ushort> _heldKeys = [];
    private readonly HashSet<ushort> _remoteKeys = [];
    private readonly HashSet<MouseButton> _remoteButtons = [];
    private readonly HashSet<MouseButton> _localButtons = [];

    public ControllerApp(Config config, byte[] masterKey)
    {
        _config = config;
        _masterKey = masterKey;
        _releaseHotkey = Hotkey.Parse(config.ReleaseHotkey);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var capture = new InputCapture(OnMouse, OnKey);
        capture.Start();
        Log.Info($"Controller started. Receiver {_config.Host}:{_config.Port}, edge {_config.Edge}, release hotkey {_config.ReleaseHotkey}.");

        var backoff = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunConnectionAsync(ct);
                backoff = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (AuthenticationFailedException)
            {
                Log.Error("Authentication failed: secrets on controller and receiver don't match.");
            }
            catch (EndOfStreamException)
            {
                // The receiver drops the connection without a reply when our Hello fails to authenticate.
                Log.Warn("Receiver closed the connection. If this repeats, check that both PCs use the same secret.");
            }
            catch (Exception ex)
            {
                Log.Warn($"Connection to {_config.Host}:{_config.Port} lost or failed: {ex.Message}");
            }
            finally
            {
                OnDisconnected();
            }

            await Task.Delay(backoff, ct).ContinueWith(_ => { });
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
    }

    private async Task RunConnectionAsync(CancellationToken ct)
    {
        using var client = new TcpClient { NoDelay = true };
        using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connectCts.CancelAfter(ConnectTimeout);
            await client.ConnectAsync(_config.Host, _config.Port, connectCts.Token);
        }

        await using var channel = await SecureChannel.ConnectAsClientAsync(client.GetStream(), _masterKey, ct);
        var outbox = Session.CreateOutbox();
        lock (_gate)
        {
            _outbox = outbox;
        }

        Log.Info($"Connected to {_config.Host}:{_config.Port}.");
        await Session.RunAsync(channel, outbox.Reader, OnMessage, ct);
    }

    private void OnMessage(Message message)
    {
        if (message is LeaveMessage leave)
        {
            lock (_gate)
            {
                if (_remote)
                {
                    _remote = false;
                    var (x, y) = Edge.PointInside(User32.GetVirtualScreen(), _config.Edge, leave.Ratio);
                    User32.SetCursorPos(x, y);
                    Log.Info("Control returned to controller.");
                }
            }
        }
    }

    private void OnDisconnected()
    {
        lock (_gate)
        {
            _outbox?.Writer.TryComplete();
            _outbox = null;
            _remoteKeys.Clear();
            _remoteButtons.Clear();
            if (_remote)
            {
                _remote = false;
                Log.Info("Control returned to controller (disconnected).");
            }
        }
    }

    private bool OnMouse(RawMouseEvent e)
    {
        lock (_gate)
        {
            switch (e.Message)
            {
                case User32.WM_MOUSEMOVE:
                    return OnMouseMove(e.X, e.Y);
                case User32.WM_MOUSEWHEEL or User32.WM_MOUSEHWHEEL:
                    if (!_remote)
                    {
                        return false;
                    }

                    Send(new WheelMessage((short)(e.MouseData >> 16), e.Message == User32.WM_MOUSEHWHEEL));
                    return true;
                default:
                    return TryMapButton(e, out var button, out var down) && OnMouseButton(button, down);
            }
        }
    }

    private bool OnMouseMove(int x, int y)
    {
        if (_remote)
        {
            // Swallowed moves never reach the cursor, so it stays parked and pt - cursor is the raw delta.
            User32.GetCursorPos(out var cursor);
            var dx = x - cursor.X;
            var dy = y - cursor.Y;
            if (dx != 0 || dy != 0)
            {
                Send(new MouseMoveMessage(ClampShort(dx), ClampShort(dy)));
            }

            return true;
        }

        var screen = User32.GetVirtualScreen();
        if (_outbox is null || _localButtons.Count > 0 || !Edge.IsAt(x, y, screen, _config.Edge))
        {
            return false;
        }

        _remote = true;
        Send(new EnterMessage(_config.Edge, Edge.RatioAlong(x, y, screen, _config.Edge)));
        var (parkX, parkY) = screen.Center;
        User32.SetCursorPos(parkX, parkY);
        Log.Info("Control passed to receiver.");
        return true;
    }

    private bool OnMouseButton(MouseButton button, bool down)
    {
        if (down)
        {
            if (!_remote)
            {
                _localButtons.Add(button);
                return false;
            }

            _remoteButtons.Add(button);
            Send(new MouseButtonMessage(button, true));
            return true;
        }

        // A release goes wherever the press went, so drags that started on one side finish there.
        if (_remoteButtons.Remove(button))
        {
            Send(new MouseButtonMessage(button, false));
            return true;
        }

        _localButtons.Remove(button);
        return false;
    }

    private bool OnKey(RawKeyEvent e)
    {
        lock (_gate)
        {
            if (e.Up)
            {
                _heldKeys.Remove(e.VirtualKey);
                if (!_remoteKeys.Remove(e.VirtualKey))
                {
                    return false;
                }

                Send(ToMessage(e));
                return true;
            }

            _heldKeys.Add(e.VirtualKey);
            if (!_remote)
            {
                return false;
            }

            if (_releaseHotkey.Matches(e.VirtualKey, _heldKeys))
            {
                ForceLocal();
                // Swallow the hotkey's own release too.
                _remoteKeys.Add(e.VirtualKey);
                return true;
            }

            _remoteKeys.Add(e.VirtualKey);
            Send(ToMessage(e));
            return true;
        }
    }

    private void ForceLocal()
    {
        _remote = false;
        Send(new ReleaseAllMessage());
        var (x, y) = Edge.PointInside(User32.GetVirtualScreen(), _config.Edge, 0.5f);
        User32.SetCursorPos(x, y);
        Log.Info("Control returned to controller (hotkey).");
    }

    private void Send(Message message) => _outbox?.Writer.TryWrite(message);

    private static KeyMessage ToMessage(RawKeyEvent e) =>
        new(e.VirtualKey, e.ScanCode, (e.Up ? KeyFlags.Up : KeyFlags.None) | (e.Extended ? KeyFlags.Extended : KeyFlags.None));

    private static short ClampShort(int value) => (short)Math.Clamp(value, short.MinValue, short.MaxValue);

    private static bool TryMapButton(RawMouseEvent e, out MouseButton button, out bool down)
    {
        (button, down) = e.Message switch
        {
            User32.WM_LBUTTONDOWN => (MouseButton.Left, true),
            User32.WM_LBUTTONUP => (MouseButton.Left, false),
            User32.WM_RBUTTONDOWN => (MouseButton.Right, true),
            User32.WM_RBUTTONUP => (MouseButton.Right, false),
            User32.WM_MBUTTONDOWN => (MouseButton.Middle, true),
            User32.WM_MBUTTONUP => (MouseButton.Middle, false),
            User32.WM_XBUTTONDOWN => ((e.MouseData >> 16) == User32.XBUTTON1 ? MouseButton.X1 : MouseButton.X2, true),
            User32.WM_XBUTTONUP => ((e.MouseData >> 16) == User32.XBUTTON1 ? MouseButton.X1 : MouseButton.X2, false),
            _ => ((MouseButton)255, false),
        };
        return (byte)button != 255;
    }
}
