using System.Runtime.InteropServices;

namespace LocalAsrClient.App.TextInjection;

internal static class NotepadPlusPlusTargetResolver
{
    private const uint NppmGetCurrentScintilla = Win32FocusNative.WmUser + 1000 + 4;

    public static IntPtr ResolveEditWindow(IntPtr rootWindow, IntPtr capturedEdit)
    {
        if (rootWindow == IntPtr.Zero
            || !Win32FocusNative.IsWindow(rootWindow)
            || !string.Equals(EditableFocusDetector.GetClassName(rootWindow), "Notepad++", StringComparison.OrdinalIgnoreCase))
        {
            return capturedEdit;
        }

        if (capturedEdit != IntPtr.Zero
            && Win32FocusNative.IsWindow(capturedEdit)
            && string.Equals(EditableFocusDetector.GetClassName(capturedEdit), "Scintilla", StringComparison.OrdinalIgnoreCase))
        {
            return capturedEdit;
        }

        var viewIndexPointer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(viewIndexPointer, 0);
            Win32FocusNative.SendMessage(rootWindow, NppmGetCurrentScintilla, IntPtr.Zero, viewIndexPointer);
            var viewIndex = Marshal.ReadInt32(viewIndexPointer);

            var scintillaWindows = new List<IntPtr>();
            Win32FocusNative.EnumChildWindows(rootWindow, (hwnd, _) =>
            {
                if (string.Equals(EditableFocusDetector.GetClassName(hwnd), "Scintilla", StringComparison.OrdinalIgnoreCase))
                {
                    scintillaWindows.Add(hwnd);
                }

                return true;
            }, IntPtr.Zero);

            if (scintillaWindows.Count == 0)
            {
                return capturedEdit;
            }

            if (viewIndex >= 0 && viewIndex < scintillaWindows.Count)
            {
                return scintillaWindows[viewIndex];
            }

            return scintillaWindows[0];
        }
        finally
        {
            Marshal.FreeHGlobal(viewIndexPointer);
        }
    }
}
