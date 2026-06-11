using System.Runtime.InteropServices;

namespace LocalAsrClient.App.TextInjection;

internal static class Win32InputNative
{
    public const int InputKeyboard = 1;
    public const ushort KeyEventFUnicode = 0x0004;
    public const ushort KeyEventFKeyUp = 0x0002;
    public const ushort VirtualKeyControl = 0x11;
    public const ushort VirtualKeyV = 0x56;

    [StructLayout(LayoutKind.Sequential)]
    public struct Input
    {
        public int Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput KeyboardInput;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);
}
