using System.Runtime.InteropServices;
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Text;

namespace LocalAsrClient.App.TextInjection;

public sealed class SendInputTextInjector : ITextInjector
{
    public Task<TextInjectionResult> TryInjectAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult(new TextInjectionResult(TextInjectionStatus.Failed, "识别文本为空。"));
        }

        var inputs = new List<Win32InputNative.Input>(text.Length * 2);
        foreach (var ch in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            inputs.Add(CreateUnicodeInput(ch, keyUp: false));
            inputs.Add(CreateUnicodeInput(ch, keyUp: true));
        }

        var sent = Win32InputNative.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Win32InputNative.Input>());
        if (sent != inputs.Count)
        {
            return Task.FromResult(new TextInjectionResult(TextInjectionStatus.Failed, $"SendInput 只发送了 {sent}/{inputs.Count} 个输入事件。"));
        }

        return Task.FromResult(new TextInjectionResult(TextInjectionStatus.Success, null));
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

