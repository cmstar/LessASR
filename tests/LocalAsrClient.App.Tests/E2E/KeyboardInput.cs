using System.Runtime.InteropServices;
using LocalAsrClient.App.TextInjection;

namespace LocalAsrClient.App.Tests.E2E;

public static class KeyboardInput
{
    // SendInput 使用通用修饰键 VK，KEYEVENTF_EXTENDEDKEY 决定右侧按键。
    private const ushort VirtualKeyRightControl = 0x11;
    private const ushort VirtualKeyRightAlt = 0x12;

    public static void PressRightControl() => Press(VirtualKeyRightControl);

    public static void PressRightAlt() => Press(VirtualKeyRightAlt);

    private static void Press(ushort virtualKey)
    {
        var inputs = new[]
        {
            Create(virtualKey, keyUp: false),
            Create(virtualKey, keyUp: true)
        };

        var sent = Win32InputNative.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<Win32InputNative.Input>());
        if (sent != inputs.Length)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"SendInput sent {sent} of {inputs.Length} inputs. Win32 error: {error}.");
        }
    }

    private static Win32InputNative.Input Create(ushort virtualKey, bool keyUp) => new()
    {
        Type = Win32InputNative.InputKeyboard,
        Union = new Win32InputNative.InputUnion
        {
            KeyboardInput = new Win32InputNative.KeyboardInput
            {
                VirtualKey = virtualKey,
                Flags = (uint)(Win32InputNative.KeyEventFExtendedKey
                    | (keyUp ? Win32InputNative.KeyEventFKeyUp : 0))
            }
        }
    };
}
