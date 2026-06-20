using System.Diagnostics;
using System.Runtime.InteropServices;
using LocalAsrClient.App.Diagnostics;
using LocalAsrClient.Core.Abstractions;

namespace LocalAsrClient.App.Hotkeys;

public sealed class GlobalHotkeyListener : IHotkeyListener
{
    private readonly int _virtualKeyCode;
    private readonly IDiagnosticEventSink _diagnostics;
    private readonly Win32HotkeyNative.LowLevelKeyboardProc _callback;
    private IntPtr _hook;
    private bool _isDown;

    public GlobalHotkeyListener(int virtualKeyCode)
        : this(virtualKeyCode, NullDiagnosticEventSink.Instance)
    {
    }

    public GlobalHotkeyListener(int virtualKeyCode, IDiagnosticEventSink diagnostics)
    {
        _virtualKeyCode = virtualKeyCode;
        _diagnostics = diagnostics;
        _callback = HookCallback;
    }

    public event Action? Triggered;
    public bool IsRunning => _hook != IntPtr.Zero;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        var moduleHandle = Win32HotkeyNative.GetModuleHandle(module.ModuleName);
        _hook = Win32HotkeyNative.SetWindowsHookEx(Win32HotkeyNative.WhKeyboardLl, _callback, moduleHandle, 0);
        if (_hook == IntPtr.Zero)
        {
            throw new InvalidOperationException("无法注册全局键盘监听。");
        }
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            Win32HotkeyNative.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _isDown = false;
    }

    public void Dispose()
    {
        Stop();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var data = Marshal.PtrToStructure<Win32HotkeyNative.KbdLlHookStruct>(lParam);
            if (data.VkCode == _virtualKeyCode)
            {
                if (message is Win32HotkeyNative.WmKeyDown or Win32HotkeyNative.WmSysKeyDown)
                {
                    if (!_isDown)
                    {
                        _isDown = true;
                        _ = _diagnostics.WriteAsync(CreateTriggeredEvent(message, data));
                        Triggered?.Invoke();
                    }

                    return (IntPtr)1;
                }

                if (message is Win32HotkeyNative.WmKeyUp or Win32HotkeyNative.WmSysKeyUp)
                {
                    _isDown = false;
                    return (IntPtr)1;
                }
            }
        }

        return Win32HotkeyNative.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static DiagnosticEvent CreateTriggeredEvent(int message, Win32HotkeyNative.KbdLlHookStruct data)
    {
        return new DiagnosticEvent(
            0,
            DateTimeOffset.Now,
            "Hotkey.Triggered",
            null,
            Environment.CurrentManagedThreadId,
            DiagnosticSnapshotCollector.Capture(),
            new Dictionary<string, string?>
            {
                ["vkCode"] = data.VkCode.ToString(),
                ["message"] = message.ToString()
            });
    }
}
