using System.Diagnostics;
using System.Runtime.InteropServices;
using LocalAsrClient.App.Diagnostics;
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Text;
using WpfClipboard = System.Windows.Clipboard;
using WpfTextDataFormat = System.Windows.TextDataFormat;

namespace LocalAsrClient.App.TextInjection;

public sealed class SendInputTextInjector : ITextInjector
{
    private const string VerificationFailedMessage = "文本注入后验证未通过。";

    private readonly InjectionTargetCapture _targetCapture;
    private readonly IDiagnosticEventSink _diagnostics;

    public SendInputTextInjector(InjectionTargetCapture targetCapture)
        : this(targetCapture, NullDiagnosticEventSink.Instance)
    {
    }

    public SendInputTextInjector(InjectionTargetCapture targetCapture, IDiagnosticEventSink diagnostics)
    {
        _targetCapture = targetCapture;
        _diagnostics = diagnostics;
    }

    public async Task<TextInjectionResult> TryInjectAsync(string text, CancellationToken cancellationToken)
    {
        var textLength = text?.Length ?? 0;
        _ = _diagnostics.WriteAsync(CreateEvent("TextInjection.Before", textLength, null));

        if (string.IsNullOrEmpty(text))
        {
            var emptyResult = new TextInjectionResult(TextInjectionStatus.Failed, "识别文本为空。");
            _ = _diagnostics.WriteAsync(CreateEvent("TextInjection.After", textLength, emptyResult.Status.ToString()));
            return emptyResult;
        }

        var hadInjectionTarget = false;
        var anyInjectionSucceeded = false;

        var editWindow = _targetCapture.GetInjectionTarget();
        if (editWindow != IntPtr.Zero && Win32FocusNative.IsWindow(editWindow))
        {
            hadInjectionTarget = true;
            var className = EditableFocusDetector.GetClassName(editWindow);
            var method = TextInjectionStrategy.Select(className);
            _ = _diagnostics.WriteAsync(CreateEvent("TextInjection.StrategySelected", text.Length, method.ToString()));

            if (TryInjectDirect(editWindow, className, text, cancellationToken))
            {
                anyInjectionSucceeded = true;
                if (TryVerifyInjection(editWindow, className, text, "Direct"))
                {
                    Debug.WriteLine($"Text injection succeeded via direct message. ClassName={className}");
                    var directResult = new TextInjectionResult(TextInjectionStatus.Success, null);
                    _ = _diagnostics.WriteAsync(CreateEvent("TextInjection.After", text.Length, directResult.Status.ToString()));
                    return directResult;
                }

                Debug.WriteLine("Text injection failed: post-injection verification did not pass after direct message.");
                var directVerifyFailedResult = new TextInjectionResult(TextInjectionStatus.Failed, VerificationFailedMessage);
                _ = _diagnostics.WriteAsync(CreateEvent("TextInjection.After", text.Length, directVerifyFailedResult.Status.ToString()));
                return directVerifyFailedResult;
            }
        }

        var rootWindow = _targetCapture.GetRootWindow();
        if (rootWindow != IntPtr.Zero
            && Win32FocusNative.IsWindow(rootWindow)
            && TryInjectViaForegroundSendInput(rootWindow, text, cancellationToken))
        {
            anyInjectionSucceeded = true;
            hadInjectionTarget = true;
            var verifyTarget = EditableFocusDetector.ResolveEditableTarget(rootWindow);
            var verifyClassName = EditableFocusDetector.GetClassName(verifyTarget);
            if (TryVerifyInjection(verifyTarget, verifyClassName, text, "SendInput"))
            {
                Debug.WriteLine($"Text injection succeeded via SendInput fallback. ClassName={verifyClassName}");
                var sendInputResult = new TextInjectionResult(TextInjectionStatus.Success, null);
                _ = _diagnostics.WriteAsync(CreateEvent("TextInjection.After", text.Length, sendInputResult.Status.ToString()));
                return sendInputResult;
            }

            Debug.WriteLine("Text injection failed: post-injection verification did not pass after SendInput.");
            var sendInputVerifyFailedResult = new TextInjectionResult(TextInjectionStatus.Failed, VerificationFailedMessage);
            _ = _diagnostics.WriteAsync(CreateEvent("TextInjection.After", text.Length, sendInputVerifyFailedResult.Status.ToString()));
            return sendInputVerifyFailedResult;
        }

        if (rootWindow != IntPtr.Zero
            && Win32FocusNative.IsWindow(rootWindow)
            && await TryInjectViaClipboardPasteAsync(rootWindow, text, cancellationToken))
        {
            anyInjectionSucceeded = true;
            hadInjectionTarget = true;
            var verifyTarget = EditableFocusDetector.ResolveEditableTarget(rootWindow);
            var verifyClassName = EditableFocusDetector.GetClassName(verifyTarget);
            if (TryVerifyInjection(verifyTarget, verifyClassName, text, "ClipboardPaste"))
            {
                Debug.WriteLine("Text injection succeeded via clipboard paste fallback.");
                var clipboardResult = new TextInjectionResult(TextInjectionStatus.Success, null);
                _ = _diagnostics.WriteAsync(CreateEvent("TextInjection.After", text.Length, clipboardResult.Status.ToString()));
                return clipboardResult;
            }

            Debug.WriteLine("Text injection failed: post-injection verification did not pass after clipboard paste.");
            var clipboardVerifyFailedResult = new TextInjectionResult(TextInjectionStatus.Failed, VerificationFailedMessage);
            _ = _diagnostics.WriteAsync(CreateEvent("TextInjection.After", text.Length, clipboardVerifyFailedResult.Status.ToString()));
            return clipboardVerifyFailedResult;
        }

        Debug.WriteLine(anyInjectionSucceeded
            ? "Text injection failed: post-injection verification did not pass."
            : "Text injection failed: no editable target or fallback failed.");
        var failedResult = new TextInjectionResult(
            TextInjectionStatus.NoEditableTarget,
            hadInjectionTarget ? VerificationFailedMessage : "未找到可输入位置。");
        _ = _diagnostics.WriteAsync(CreateEvent("TextInjection.After", text.Length, failedResult.Status.ToString()));
        return failedResult;
    }

    private bool TryVerifyInjection(IntPtr hwnd, string className, string text, string method)
    {
        var readBack = InjectionTextVerifier.TryReadText(hwnd, className);
        var verified = InjectionTextVerifier.ContainsInjectedText(readBack, text);
        _ = _diagnostics.WriteAsync(CreateVerifyEvent(method, verified, readBack?.Length ?? 0, text.Length));
        return verified;
    }

    private DiagnosticEvent CreateEvent(string eventName, int textLength, string? state)
    {
        return new DiagnosticEvent(
            0,
            DateTimeOffset.Now,
            eventName,
            state,
            Environment.CurrentManagedThreadId,
            DiagnosticSnapshotCollector.Capture(),
            new Dictionary<string, string?>
            {
                ["textLength"] = textLength.ToString()
            });
    }

    private DiagnosticEvent CreateVerifyEvent(string method, bool verified, int readBackLength, int textLength)
    {
        return new DiagnosticEvent(
            0,
            DateTimeOffset.Now,
            "TextInjection.Verify",
            verified ? "Success" : "Failed",
            Environment.CurrentManagedThreadId,
            DiagnosticSnapshotCollector.Capture(),
            new Dictionary<string, string?>
            {
                ["method"] = method,
                ["verified"] = verified.ToString(),
                ["readBackLength"] = readBackLength.ToString(),
                ["textLength"] = textLength.ToString()
            });
    }

    private static bool TryInjectDirect(IntPtr editWindow, string className, string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Win32FocusNative.IsWindow(editWindow))
        {
            return false;
        }

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
        if (!Win32FocusNative.IsWindow(rootWindow))
        {
            return false;
        }

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

        if (!Win32FocusNative.IsWindow(rootWindow))
        {
            return false;
        }

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
        if (!Win32FocusNative.IsWindow(rootWindow))
        {
            return false;
        }

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
