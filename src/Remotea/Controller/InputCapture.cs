using System.Runtime.InteropServices;
using Remotea.Native;

namespace Remotea.Controller;

internal readonly record struct RawMouseEvent(int Message, int X, int Y, uint MouseData);

internal readonly record struct RawKeyEvent(ushort VirtualKey, ushort ScanCode, bool Up, bool Extended);

/// <summary>
/// Installs low-level mouse and keyboard hooks on a dedicated thread with its own message loop.
/// Handlers run on that thread, must be fast, and return true to swallow the event.
/// Injected events (from SendInput/SetCursorPos) always pass through untouched.
/// </summary>
internal sealed class InputCapture : IDisposable
{
    private readonly Func<RawMouseEvent, bool> _onMouse;
    private readonly Func<RawKeyEvent, bool> _onKey;
    private readonly User32.LowLevelProc _mouseProc;
    private readonly User32.LowLevelProc _keyProc;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _mouseHook;
    private IntPtr _keyHook;

    public InputCapture(Func<RawMouseEvent, bool> onMouse, Func<RawKeyEvent, bool> onKey)
    {
        _onMouse = onMouse;
        _onKey = onKey;
        // Keep delegates in fields so the GC doesn't collect them while native code holds the pointers.
        _mouseProc = MouseProc;
        _keyProc = KeyProc;
    }

    public void Start()
    {
        using var ready = new ManualResetEventSlim();
        Exception? error = null;
        _thread = new Thread(() =>
        {
            try
            {
                _threadId = User32.GetCurrentThreadId();
                var module = User32.GetModuleHandle(null);
                _mouseHook = User32.SetWindowsHookEx(User32.WH_MOUSE_LL, _mouseProc, module, 0);
                _keyHook = User32.SetWindowsHookEx(User32.WH_KEYBOARD_LL, _keyProc, module, 0);
                if (_mouseHook == IntPtr.Zero || _keyHook == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"SetWindowsHookEx failed (error {Marshal.GetLastWin32Error()}).");
                }
            }
            catch (Exception ex)
            {
                error = ex;
                ready.Set();
                return;
            }

            ready.Set();
            while (User32.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
            {
            }

            User32.UnhookWindowsHookEx(_mouseHook);
            User32.UnhookWindowsHookEx(_keyHook);
        })
        {
            IsBackground = true,
            Name = "InputHooks",
        };
        _thread.Start();
        ready.Wait();
        if (error is not null)
        {
            throw error;
        }
    }

    public void Dispose()
    {
        if (_thread is { IsAlive: true })
        {
            User32.PostThreadMessage(_threadId, User32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == User32.HC_ACTION)
        {
            var data = Marshal.PtrToStructure<User32.MSLLHOOKSTRUCT>(lParam);
            if ((data.flags & User32.LLMHF_INJECTED) == 0 &&
                Invoke(_onMouse, new RawMouseEvent((int)wParam, data.pt.X, data.pt.Y, data.mouseData)))
            {
                return 1;
            }
        }

        return User32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr KeyProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == User32.HC_ACTION)
        {
            var data = Marshal.PtrToStructure<User32.KBDLLHOOKSTRUCT>(lParam);
            var message = (int)wParam;
            var up = message is User32.WM_KEYUP or User32.WM_SYSKEYUP;
            if ((data.flags & User32.LLKHF_INJECTED) == 0 &&
                Invoke(_onKey, new RawKeyEvent((ushort)data.vkCode, (ushort)data.scanCode, up, (data.flags & User32.LLKHF_EXTENDED) != 0)))
            {
                return 1;
            }
        }

        return User32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private static bool Invoke<T>(Func<T, bool> handler, T e)
    {
        try
        {
            return handler(e);
        }
        catch (Exception ex)
        {
            Log.Error($"Input handler failed: {ex.Message}");
            return false;
        }
    }
}
