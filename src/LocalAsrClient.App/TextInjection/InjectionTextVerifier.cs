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

        return TryReadStandardEditText(hwnd);
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

        var buffer = new StringBuilder(length + 1);
        Win32FocusNative.SendMessageGetText(
            hwnd,
            (uint)(Win32FocusNative.WmUser + Win32FocusNative.SciGetText),
            (IntPtr)(length + 1),
            buffer);
        return buffer.ToString();
    }
}
