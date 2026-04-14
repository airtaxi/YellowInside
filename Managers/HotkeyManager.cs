using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace YellowInside;

public sealed partial class HotkeyManager : IDisposable
{
    public const uint ModifierAlt = 0x0001;
    public const uint ModifierControl = 0x0002;
    public const uint ModifierShift = 0x0004;
    public const uint ModifierWin = 0x0008;

    private const int HotkeyId = 9001;
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_APP = 0x8000;
    private const uint WM_UPDATE_HOTKEY = WM_APP + 100;

    private Thread _thread;
    private uint _threadId;
    private bool _running;
    private uint _modifiers;
    private uint _virtualKey;

    public event Action<nint> HotkeyPressed;

    public bool IsRunning => _running;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hwnd, int id);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessage(out NativeMessage message, nint hwnd, uint filterMin, uint filterMax);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }

    public void Start(uint modifiers, uint virtualKey)
    {
        if (_running) Stop();

        _modifiers = modifiers;
        _virtualKey = virtualKey;

        using var signal = new ManualResetEventSlim(false);
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            RegisterHotKey(0, HotkeyId, _modifiers, _virtualKey);
            signal.Set();

            while (GetMessage(out var nativeMessage, 0, 0, 0) > 0)
            {
                if (nativeMessage.Message == WM_HOTKEY && nativeMessage.WParam == HotkeyId)
                {
                    var foregroundWindow = GetForegroundWindow();
                    HotkeyPressed?.Invoke(foregroundWindow);
                }
                else if (nativeMessage.Message == WM_UPDATE_HOTKEY)
                {
                    UnregisterHotKey(0, HotkeyId);
                    RegisterHotKey(0, HotkeyId, _modifiers, _virtualKey);
                }
            }

            UnregisterHotKey(0, HotkeyId);
        })
        {
            IsBackground = true,
            Name = "HotkeyListener",
        };
        _thread.Start();
        signal.Wait();
        _running = true;
    }

    public void UpdateHotkey(uint modifiers, uint virtualKey)
    {
        _modifiers = modifiers;
        _virtualKey = virtualKey;
        if (_running && _threadId != 0)
            PostThreadMessage(_threadId, WM_UPDATE_HOTKEY, 0, 0);
    }

    public void Stop()
    {
        if (!_running) return;
        if (_threadId != 0)
            PostThreadMessage(_threadId, 0x0012, 0, 0); // WM_QUIT
        _thread?.Join(TimeSpan.FromSeconds(2));
        _running = false;
        _threadId = 0;
        _thread = null;
    }

    public void Dispose() => Stop();
}
