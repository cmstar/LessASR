using System.Runtime.InteropServices;
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Text;

namespace LocalAsrClient.App.TextInjection;

public sealed class SendInputTextInjector : ITextInjector
{
    private static readonly HashSet<string> ReplaceSelClassNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Edit",
        "RICHEDIT",
        "RichEdit20W",
        "RichEdit50W",
        "RICHEDIT50W",
        "RichEditD2DPT",
        "RICHEDIT60W",
        "ThunderRT6TextBox",
    };

    private readonly InjectionTargetCapture _targetCapture;

    public SendInputTextInjector(InjectionTargetCapture targetCapture)
    {
        _targetCapture = targetCapture;
    }

    public Task<TextInjectionResult> TryInjectAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult(new TextInjectionResult(TextInjectionStatus.Failed, "识别文本为空。"));
        }

        var editWindow = _targetCapture.GetInjectionTarget();
        if (editWindow == IntPtr.Zero)
        {
            return Task.FromResult(new TextInjectionResult(TextInjectionStatus.NoEditableTarget, "未找到可输入位置。"));
        }

        var className = EditableFocusDetector.GetClassName(editWindow);
        if (TryInjectDirect(editWindow, className, text, cancellationToken))
        {
            return Task.FromResult(new TextInjectionResult(TextInjectionStatus.Success, null));
        }

        var rootWindow = _targetCapture.GetRootWindow();
        if (rootWindow != IntPtr.Zero
            && TryInjectViaForegroundSendInput(rootWindow, text, cancellationToken))
        {
            return Task.FromResult(new TextInjectionResult(TextInjectionStatus.Success, null));
        }

        return Task.FromResult(new TextInjectionResult(TextInjectionStatus.NoEditableTarget, "未找到可输入位置。"));
    }

    private static bool TryInjectDirect(IntPtr editWindow, string className, string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.Equals(className, "Scintilla", StringComparison.OrdinalIgnoreCase))
        {
            Win32FocusNative.SendMessageString(
                editWindow,
                (uint)(Win32FocusNative.WmUser + Win32FocusNative.SciReplaceSel),
                IntPtr.Zero,
                text);
            return true;
        }

        if (ReplaceSelClassNames.Contains(className))
        {
            Win32FocusNative.SendMessageString(editWindow, Win32FocusNative.EmReplaceSel, (IntPtr)1, text);
            return true;
        }

        foreach (var ch in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Win32FocusNative.SendMessage(editWindow, Win32FocusNative.WmChar, (IntPtr)ch, IntPtr.Zero);
        }

        return true;
    }

    private static bool TryInjectViaForegroundSendInput(IntPtr rootWindow, string text, CancellationToken cancellationToken)
    {
        Win32FocusNative.AllowSetForegroundWindow(Win32FocusNative.AsfwAny);
        if (!EditableFocusDetector.TryActivateForInjection(rootWindow, rootWindow))
        {
            return false;
        }

        var inputs = new List<Win32InputNative.Input>(text.Length * 2);
        foreach (var ch in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            inputs.Add(CreateUnicodeInput(ch, keyUp: false));
            inputs.Add(CreateUnicodeInput(ch, keyUp: true));
        }

        var sent = Win32InputNative.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Win32InputNative.Input>());
        return sent == inputs.Count;
    }

    private static Win32InputNative.Input CreateUnicodeInput(char ch, bool keyUp)
    {
        return new Win32InputNative.Input
        {
            Type = Win32InputNative.InputKeyboard,
            Union = new Win32InputNative.InputUnion
            {
                KeyboardInput = new Win32InputNative.KeyboardInput
                {
                    VirtualKey = 0,
                    ScanCode = ch,
                    Flags = (uint)(Win32InputNative.KeyEventFUnicode | (keyUp ? Win32InputNative.KeyEventFKeyUp : (ushort)0)),
                    Time = 0,
                    ExtraInfo = IntPtr.Zero
                }
            }
        };
    }
}
