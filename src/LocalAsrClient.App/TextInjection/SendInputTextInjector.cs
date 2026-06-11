using System.Diagnostics;
using System.Runtime.InteropServices;
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Text;
using WpfClipboard = System.Windows.Clipboard;
using WpfTextDataFormat = System.Windows.TextDataFormat;

namespace LocalAsrClient.App.TextInjection;

public sealed class SendInputTextInjector : ITextInjector
{
    private readonly InjectionTargetCapture _targetCapture;

    public SendInputTextInjector(InjectionTargetCapture targetCapture)
    {
        _targetCapture = targetCapture;
    }

    public async Task<TextInjectionResult> TryInjectAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new TextInjectionResult(TextInjectionStatus.Failed, "识别文本为空。");
        }

        var editWindow = _targetCapture.GetInjectionTarget();
        if (editWindow != IntPtr.Zero)
        {
            var className = EditableFocusDetector.GetClassName(editWindow);
            if (TryInjectDirect(editWindow, className, text, cancellationToken))
            {
                Debug.WriteLine($"Text injection succeeded via direct message. ClassName={className}");
                return new TextInjectionResult(TextInjectionStatus.Success, null);
            }

            var directRootWindow = _targetCapture.GetRootWindow();
            if (directRootWindow != IntPtr.Zero
                && TryInjectViaForegroundSendInput(directRootWindow, text, cancellationToken))
            {
                Debug.WriteLine($"Text injection succeeded via SendInput fallback. ClassName={className}");
                return new TextInjectionResult(TextInjectionStatus.Success, null);
            }
        }

        var rootWindow = _targetCapture.GetRootWindow();
        if (rootWindow != IntPtr.Zero
            && await TryInjectViaClipboardPasteAsync(rootWindow, text, cancellationToken))
        {
            Debug.WriteLine("Text injection succeeded via clipboard paste fallback.");
            return new TextInjectionResult(TextInjectionStatus.Success, null);
        }

        Debug.WriteLine("Text injection failed: no editable target or fallback failed.");
        return new TextInjectionResult(TextInjectionStatus.NoEditableTarget, "未找到可输入位置。");
    }

    private static bool TryInjectDirect(IntPtr editWindow, string className, string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var method = TextInjectionStrategy.Select(className);
        if (method == TextInjectionMethod.ScintillaReplaceSelectionMessage)
        {
            Win32FocusNative.SendMessageString(
                editWindow,
                (uint)(Win32FocusNative.WmUser + Win32FocusNative.SciReplaceSel),
                IntPtr.Zero,
                text);
            return true;
        }

        if (method == TextInjectionMethod.ReplaceSelectionMessage)
        {
            Win32FocusNative.SendMessageString(editWindow, Win32FocusNative.EmReplaceSel, (IntPtr)1, text);
            return true;
        }

        return false;
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

    private static async Task<bool> TryInjectViaClipboardPasteAsync(
        IntPtr rootWindow,
        string text,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var originalClipboard = await RunInStaThreadAsync(() =>
        {
            try
            {
                return ClipboardBackup.Capture(WpfClipboard.GetDataObject());
            }
            catch (ExternalException)
            {
                return ClipboardBackup.Capture(null);
            }
        });

        var clipboardUpdated = await RunInStaThreadAsync(() =>
        {
            try
            {
                WpfClipboard.SetText(text, WpfTextDataFormat.UnicodeText);
                return true;
            }
            catch (ExternalException)
            {
                return false;
            }
        });
        if (!clipboardUpdated)
        {
            return false;
        }

        try
        {
            var pasted = TrySendPasteShortcut(rootWindow);
            await Task.Delay(TimeSpan.FromMilliseconds(250), CancellationToken.None);
            return pasted;
        }
        finally
        {
            await RunInStaThreadAsync(() =>
            {
                try
                {
                    if (originalClipboard.IsEmpty)
                    {
                        WpfClipboard.Clear();
                    }
                    else
                    {
                        WpfClipboard.SetDataObject(originalClipboard.ToDataObject(), copy: true);
                    }
                }
                catch (ExternalException)
                {
                }
            });
        }
    }

    private static bool TrySendPasteShortcut(IntPtr rootWindow)
    {
        Win32FocusNative.AllowSetForegroundWindow(Win32FocusNative.AsfwAny);
        if (!EditableFocusDetector.TryActivateForInjection(rootWindow, rootWindow))
        {
            return false;
        }

        var inputs = new[]
        {
            CreateVirtualKeyInput(Win32InputNative.VirtualKeyControl, keyUp: false),
            CreateVirtualKeyInput(Win32InputNative.VirtualKeyV, keyUp: false),
            CreateVirtualKeyInput(Win32InputNative.VirtualKeyV, keyUp: true),
            CreateVirtualKeyInput(Win32InputNative.VirtualKeyControl, keyUp: true),
        };

        var sent = Win32InputNative.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32InputNative.Input>());
        return sent == inputs.Length;
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

    private static Win32InputNative.Input CreateVirtualKeyInput(ushort virtualKey, bool keyUp)
    {
        return new Win32InputNative.Input
        {
            Type = Win32InputNative.InputKeyboard,
            Union = new Win32InputNative.InputUnion
            {
                KeyboardInput = new Win32InputNative.KeyboardInput
                {
                    VirtualKey = virtualKey,
                    ScanCode = 0,
                    Flags = keyUp ? Win32InputNative.KeyEventFKeyUp : 0u,
                    Time = 0,
                    ExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    private static Task<T> RunInStaThreadAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>();
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(action());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static Task RunInStaThreadAsync(Action action)
    {
        return RunInStaThreadAsync(() =>
        {
            action();
            return true;
        });
    }
}
