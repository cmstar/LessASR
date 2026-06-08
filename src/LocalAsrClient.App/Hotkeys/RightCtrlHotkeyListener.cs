using System.Diagnostics;
using System.Runtime.InteropServices;
using LocalAsrClient.Core.Abstractions;

namespace LocalAsrClient.App.Hotkeys;

public sealed class RightCtrlHotkeyListener : IHotkeyListener
{
    private readonly Win32HotkeyNative.LowLevelKeyboardProc _callback;
    private IntPtr _hook;
    private bool _isDown;

    public RightCtrlHotkeyListener()
    {
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
            throw new InvalidOperationException("无法注册右 Ctrl 全局键盘监听。");
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
            if ((message == Win32HotkeyNative.WmKeyDown || message == Win32HotkeyNative.WmSysKeyDown)
                && data.VkCode == Win32HotkeyNative.VkRControl)
            {
                if (!_isDown)
                {
                    _isDown = true;
                    Triggered?.Invoke();
                }
            }
            else if (data.VkCode == Win32HotkeyNative.VkRControl)
            {
                _isDown = false;
            }
        }

        return Win32HotkeyNative.CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
