namespace LocalAsrClient.App.TextInjection;

internal static class EditableFocusDetector
{
    private static readonly HashSet<string> EditableClassNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Edit",
        "RICHEDIT",
        "RichEdit20W",
        "RichEdit50W",
        "RICHEDIT50W",
        "RichEditD2DPT",
        "RICHEDIT60W",
        "ThunderRT6TextBox",
        "Scintilla",
    };

    public static IntPtr GetFocusedWindowFromGuiThreadInfo(IntPtr rootWindow)
    {
        if (rootWindow == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var threadId = Win32FocusNative.GetWindowThreadProcessId(rootWindow, out _);
        var info = new Win32FocusNative.GuiThreadInfo
        {
            CbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32FocusNative.GuiThreadInfo>()
        };

        return Win32FocusNative.GetGUIThreadInfo(threadId, ref info)
            ? info.HwndFocus
            : IntPtr.Zero;
    }

    public static string GetClassName(IntPtr hwnd)
    {
        var buffer = new System.Text.StringBuilder(256);
        return Win32FocusNative.GetClassName(hwnd, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : string.Empty;
    }

    public static IntPtr ResolveEditableTarget(IntPtr rootWindow)
    {
        if (rootWindow == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var focused = GetFocusedWindowFromGuiThreadInfo(rootWindow);
        if (!IsEditableWindow(focused))
        {
            focused = GetFocusedControlInWindow(rootWindow);
        }
        if (IsEditableWindow(focused))
        {
            return focused;
        }

        if (IsEditableWindow(rootWindow))
        {
            return rootWindow;
        }

        return FindEditableDescendant(rootWindow);
    }

    public static bool IsEditableWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Win32FocusNative.IsWindow(hwnd))
        {
            return false;
        }

        var className = GetClassName(hwnd);
        if (!EditableClassNames.Contains(className))
        {
            return false;
        }

        if (TextInjectionStrategy.IsRichEditClassName(className))
        {
            // RichEdit 的 EM_GETREADONLY 在部分宿主（如 Win11 记事本）上不可靠，改用窗口样式判断。
            var style = Win32FocusNative.GetWindowLong(hwnd, Win32FocusNative.GwlStyle);
            return (style & Win32FocusNative.EsReadOnly) == 0;
        }

        var readOnly = Win32FocusNative.SendMessage(hwnd, Win32FocusNative.EmGetReadOnly, IntPtr.Zero, IntPtr.Zero);
        return readOnly == IntPtr.Zero;
    }

    public static bool TryActivateForInjection(IntPtr rootWindow, IntPtr editWindow)
    {
        if (rootWindow == IntPtr.Zero || editWindow == IntPtr.Zero)
        {
            return false;
        }

        var foreground = Win32FocusNative.GetForegroundWindow();
        var foregroundThread = Win32FocusNative.GetWindowThreadProcessId(foreground, out _);
        var targetThread = Win32FocusNative.GetWindowThreadProcessId(rootWindow, out _);
        var currentThread = Win32FocusNative.GetCurrentThreadId();

        var attachedForeground = false;
        var attachedTarget = false;
        if (foregroundThread != currentThread)
        {
            attachedForeground = Win32FocusNative.AttachThreadInput(currentThread, foregroundThread, attach: true);
        }

        if (targetThread != currentThread)
        {
            attachedTarget = Win32FocusNative.AttachThreadInput(currentThread, targetThread, attach: true);
        }

        try
        {
            Win32FocusNative.SetForegroundWindow(rootWindow);
            Win32FocusNative.SetFocus(editWindow);
            return Win32FocusNative.GetFocus() == editWindow
                || Win32FocusNative.GetForegroundWindow() == rootWindow;
        }
        finally
        {
            if (attachedTarget)
            {
                Win32FocusNative.AttachThreadInput(currentThread, targetThread, attach: false);
            }

            if (attachedForeground)
            {
                Win32FocusNative.AttachThreadInput(currentThread, foregroundThread, attach: false);
            }
        }
    }

    private static IntPtr GetFocusedControlInWindow(IntPtr rootWindow)
    {
        var threadId = Win32FocusNative.GetWindowThreadProcessId(rootWindow, out _);
        var currentThread = Win32FocusNative.GetCurrentThreadId();
        var attached = false;
        if (threadId != currentThread)
        {
            attached = Win32FocusNative.AttachThreadInput(currentThread, threadId, attach: true);
        }

        try
        {
            return Win32FocusNative.GetFocus();
        }
        finally
        {
            if (attached)
            {
                Win32FocusNative.AttachThreadInput(currentThread, threadId, attach: false);
            }
        }
    }

    private static IntPtr FindEditableDescendant(IntPtr rootWindow)
    {
        IntPtr found = IntPtr.Zero;
        Win32FocusNative.EnumChildWindows(rootWindow, (hwnd, _) =>
        {
            if (!IsEditableWindow(hwnd))
            {
                return true;
            }

            found = hwnd;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    private static bool IsEditableClassName(IntPtr hwnd)
    {
        return EditableClassNames.Contains(GetClassName(hwnd));
    }
}
