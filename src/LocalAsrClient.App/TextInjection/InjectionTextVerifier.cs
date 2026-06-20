using System.Runtime.InteropServices;
using System.Text;

namespace LocalAsrClient.App.TextInjection;

internal static class InjectionTextVerifier
{
    public static bool ContainsInjectedText(string? readBack, string injected)
    {
        return !string.IsNullOrEmpty(readBack)
            && readBack.Contains(injected, StringComparison.Ordinal);
    }

    public static bool CanReadBackText(string className)
    {
        var method = TextInjectionStrategy.Select(className);
        return method is TextInjectionMethod.ReplaceSelectionMessage
            or TextInjectionMethod.ScintillaReplaceSelectionMessage;
    }

    public static string? TryReadText(IntPtr hwnd, string className)
    {
        if (hwnd == IntPtr.Zero || !Win32FocusNative.IsWindow(hwnd))
        {
            return null;
        }

        if (!CanReadBackText(className))
        {
            return null;
        }

        if (string.Equals(className, "Scintilla", StringComparison.OrdinalIgnoreCase))
        {
            return TryReadScintillaText(hwnd);
        }

        if (TextInjectionStrategy.IsRichEditClassName(className))
        {
            return TryReadRichEditText(hwnd);
        }

        return TryReadStandardEditText(hwnd);
    }

    private static string TryReadRichEditText(IntPtr hwnd)
    {
        var viaGetText = TryReadStandardEditText(hwnd);
        if (!string.IsNullOrEmpty(viaGetText))
        {
            return viaGetText;
        }

        var length = (int)Win32FocusNative.SendMessage(hwnd, Win32FocusNative.EmGetTextLength, IntPtr.Zero, IntPtr.Zero);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = Marshal.AllocHGlobal((length + 1) * sizeof(char));
        try
        {
            var range = new Win32FocusNative.TextRange
            {
                Chrg = new Win32FocusNative.CharRange
                {
                    CpMin = 0,
                    CpMax = -1
                },
                LpstrText = buffer
            };
            Win32FocusNative.SendMessage(hwnd, Win32FocusNative.EmGetTextRange, IntPtr.Zero, ref range);
            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string TryReadStandardEditText(IntPtr hwnd)
    {
        var length = (int)Win32FocusNative.SendMessage(hwnd, Win32FocusNative.WmGetTextLength, IntPtr.Zero, IntPtr.Zero);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(length + 1);
        Win32FocusNative.SendMessageGetText(hwnd, Win32FocusNative.WmGetText, (IntPtr)(length + 1), buffer);
        return buffer.ToString();
    }

    private static string TryReadScintillaText(IntPtr hwnd)
    {
        var length = (int)Win32FocusNative.SendMessage(
            hwnd,
            (uint)(Win32FocusNative.WmUser + Win32FocusNative.SciGetTextLength),
            IntPtr.Zero,
            IntPtr.Zero);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = Marshal.AllocHGlobal(length + 1);
        try
        {
            Win32FocusNative.SendMessage(
                hwnd,
                (uint)(Win32FocusNative.WmUser + Win32FocusNative.SciGetText),
                (IntPtr)(length + 1),
                buffer);
            var bytes = new byte[length];
            Marshal.Copy(buffer, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
