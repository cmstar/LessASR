using System.Runtime.InteropServices;
using LocalAsrClient.App.TextInjection;

namespace LocalAsrClient.App.Tests.E2E;

public static class KeyboardInput
{
    private const ushort VirtualKeyRightControl = 0xA3;

    public static void PressRightControl()
    {
        var inputs = new[]
        {
            Create(VirtualKeyRightControl, keyUp: false),
            Create(VirtualKeyRightControl, keyUp: true)
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
                Flags = keyUp ? Win32InputNative.KeyEventFKeyUp : (ushort)0
            }
        }
    };
}
