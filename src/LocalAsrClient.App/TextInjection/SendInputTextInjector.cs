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
        var rootWindow = _targetCapture.GetRootWindow();
        var editWindow = FileExplorerTargetResolver.ResolveInjectionTarget(
            rootWindow,
            NotepadPlusPlusTargetResolver.ResolveEditWindow(
                rootWindow,
                _targetCapture.GetInjectionTarget()),
            _targetCapture.RawFocusWindow);
        var editClassName = EditableFocusDetector.GetClassName(editWindow);
        var useExplorerClipboardOnly = FileExplorerInjectionPolicy.ShouldUseClipboardOnly(rootWindow, editClassName);

        if (FileExplorerInjectionPolicy.IsExplorerWindow(rootWindow) && editWindow == IntPtr.Zero)
        {
            var explorerNoFocusResult = new TextInjectionResult(TextInjectionStatus.NoEditableTarget, "未找到可输入位置。");
            _ = _diagnostics.WriteAsync(CreateEvent("TextInjection.After", text.Length, explorerNoFocusResult.Status.ToString()));
            return explorerNoFocusResult;
        }

        var supportsDirectInject = editWindow != IntPtr.Zero
            && Win32FocusNative.IsWindow(editWindow)
            && !useExplorerClipboardOnly
            && TextInjectionStrategy.Select(editClassName) is TextInjectionMethod.ReplaceSelectionMessage
                or TextInjectionMethod.ScintillaReplaceSelectionMessage;

        if (supportsDirectInject)
        {
            hadInjectionTarget = true;
            _ = _diagnostics.WriteAsync(CreateEvent(
                "TextInjection.StrategySelected",
                text.Length,
                TextInjectionStrategy.Select(editClassName).ToString()));

            if (TryInjectDirect(rootWindow, editWindow, editClassName, text, cancellationToken))
            {
                if (TryVerifyInjection(editWindow, editClassName, text, "Direct"))
                {
                    Debug.WriteLine($"Text injection succeeded via direct message. ClassName={editClassName}");
                    return CompleteSuccess(text);
                }

                if (TextInjectionStrategy.TrustDirectWithoutVerification(editClassName))
                {
                    Debug.WriteLine(
                        "Text injection verification did not pass after direct message; trusting RichEdit write.");
                    return CompleteSuccess(text);
                }

                Debug.WriteLine("Text injection verification did not pass after Scintilla direct message; trying clipboard paste.");
            }

            Debug.WriteLine("Text injection direct message failed; trying clipboard paste fallback.");
        }

        if (rootWindow == IntPtr.Zero || !Win32FocusNative.IsWindow(rootWindow))
        {
            var noTargetResult = new TextInjectionResult(
                TextInjectionStatus.NoEditableTarget,
                hadInjectionTarget ? VerificationFailedMessage : "未找到可输入位置。");
            _ = _diagnostics.WriteAsync(CreateEvent("TextInjection.After", text.Length, noTargetResult.Status.ToString()));
            return noTargetResult;
        }

        hadInjectionTarget = true;
        var pasteTarget = editWindow != IntPtr.Zero && Win32FocusNative.IsWindow(editWindow)
            ? editWindow
            : EditableFocusDetector.ResolveEditableTarget(rootWindow);
        var pasteClassName = EditableFocusDetector.GetClassName(pasteTarget);

        if (!supportsDirectInject)
        {
            _ = _diagnostics.WriteAsync(CreateEvent(
                "TextInjection.StrategySelected",
                text.Length,
                TextInjectionMethod.ClipboardPaste.ToString()));
        }

        if (await TryInjectViaClipboardPasteAsync(rootWindow, pasteTarget, text, cancellationToken))
        {
            if (TryVerifyInjection(pasteTarget, pasteClassName, text, "ClipboardPaste"))
            {
                Debug.WriteLine("Text injection succeeded via clipboard paste fallback.");
                return CompleteSuccess(text);
            }

            if (InjectionVerificationPolicy.TrustClipboardWithoutVerification(pasteClassName))
            {
                _ = _diagnostics.WriteAsync(CreateVerifyEvent(
                    "ClipboardPaste",
                    verified: true,
                    readBackLength: 0,
                    text.Length,
                    skipped: true));
                Debug.WriteLine("Text injection succeeded via clipboard paste; Scintilla read-back is unreliable cross-process.");
                return CompleteSuccess(text);
            }

            if (!InjectionVerificationPolicy.IsVerificationRequired(pasteClassName)
                && !FileExplorerInjectionPolicy.IsExplorerWindow(rootWindow))
            {
                Debug.WriteLine("Text injection succeeded via clipboard paste with verification skipped.");
                return CompleteSuccess(text);
            }

            Debug.WriteLine("Text injection failed: post-injection verification did not pass after clipboard paste.");
            var clipboardVerifyFailedResult = new TextInjectionResult(TextInjectionStatus.Failed, VerificationFailedMessage);
            _ = _diagnostics.WriteAsync(CreateEvent("TextInjection.After", text.Length, clipboardVerifyFailedResult.Status.ToString()));
            return clipboardVerifyFailedResult;
        }

        Debug.WriteLine("Text injection failed: no editable target or fallback failed.");
        var failedResult = new TextInjectionResult(
            TextInjectionStatus.NoEditableTarget,
            hadInjectionTarget ? VerificationFailedMessage : "未找到可输入位置。");
        _ = _diagnostics.WriteAsync(CreateEvent("TextInjection.After", text.Length, failedResult.Status.ToString()));
        return failedResult;
    }

    private TextInjectionResult CompleteSuccess(string text)
    {
        var successResult = new TextInjectionResult(TextInjectionStatus.Success, null);
        _ = _diagnostics.WriteAsync(CreateEvent("TextInjection.After", text.Length, successResult.Status.ToString()));
        return successResult;
    }

    private bool TryVerifyInjection(IntPtr hwnd, string className, string text, string method)
    {
        if (!InjectionVerificationPolicy.IsVerificationRequired(className))
        {
            var skippedOk = hwnd != IntPtr.Zero;
            _ = _diagnostics.WriteAsync(CreateVerifyEvent(
                method,
                verified: skippedOk,
                readBackLength: 0,
                text.Length,
                skipped: true));
            return skippedOk;
        }

        var readBack = InjectionTextVerifier.TryReadText(hwnd, className);
        var verified = InjectionVerificationPolicy.IsInjectionVerified(className, readBack, text);
        _ = _diagnostics.WriteAsync(CreateVerifyEvent(method, verified, readBack?.Length ?? 0, text.Length, skipped: false));
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

    private DiagnosticEvent CreateVerifyEvent(
        string method,
        bool verified,
        int readBackLength,
        int textLength,
        bool skipped)
    {
        return new DiagnosticEvent(
            0,
            DateTimeOffset.Now,
            "TextInjection.Verify",
            skipped ? "Skipped" : verified ? "Success" : "Failed",
            Environment.CurrentManagedThreadId,
            DiagnosticSnapshotCollector.Capture(),
            new Dictionary<string, string?>
            {
                ["method"] = method,
                ["verified"] = verified.ToString(),
                ["skipped"] = skipped.ToString(),
                ["readBackLength"] = readBackLength.ToString(),
                ["textLength"] = textLength.ToString()
            });
    }

    private static bool TryInjectDirect(
        IntPtr rootWindow,
        IntPtr editWindow,
        string className,
        string text,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Win32FocusNative.IsWindow(editWindow))
        {
            return false;
        }

        var method = TextInjectionStrategy.Select(className);
        if (method is not (TextInjectionMethod.ReplaceSelectionMessage or TextInjectionMethod.ScintillaReplaceSelectionMessage))
        {
            return false;
        }

        var activationRoot = rootWindow != IntPtr.Zero && Win32FocusNative.IsWindow(rootWindow)
            ? rootWindow
            : editWindow;
        Win32FocusNative.AllowSetForegroundWindow(Win32FocusNative.AsfwAny);
        if (!EditableFocusDetector.TryActivateForInjection(activationRoot, editWindow))
        {
            return false;
        }

        if (method == TextInjectionMethod.ScintillaReplaceSelectionMessage)
        {
            var currentPos = Win32FocusNative.SendMessage(
                editWindow,
                (uint)(Win32FocusNative.WmUser + Win32FocusNative.SciGetCurrentPos),
                IntPtr.Zero,
                IntPtr.Zero);
            Win32FocusNative.SendMessageString(
                editWindow,
                (uint)(Win32FocusNative.WmUser + Win32FocusNative.SciInsertText),
                currentPos,
                text);
            return true;
        }

        Win32FocusNative.SendMessageString(editWindow, Win32FocusNative.EmReplaceSel, (IntPtr)1, text);
        return true;
    }

    private static async Task<bool> TryInjectViaClipboardPasteAsync(
        IntPtr rootWindow,
        IntPtr editWindow,
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
            var pasted = TrySendPasteShortcut(rootWindow, editWindow);
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

                return true;
            });
        }
    }

    private static bool TrySendPasteShortcut(IntPtr rootWindow, IntPtr editWindow)
    {
        if (!Win32FocusNative.IsWindow(rootWindow))
        {
            return false;
        }

        var activationTarget = editWindow != IntPtr.Zero && Win32FocusNative.IsWindow(editWindow)
            ? editWindow
            : rootWindow;
        Win32FocusNative.AllowSetForegroundWindow(Win32FocusNative.AsfwAny);
        if (!EditableFocusDetector.TryActivateForInjection(rootWindow, activationTarget))
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
}
