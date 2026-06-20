using System.Runtime.InteropServices;
using LocalAsrClient.App.TextInjection;

namespace LocalAsrClient.App.Tests.E2E;

public sealed class InputStructSizeTests
{
    [Fact]
    public void Win32InputNative_InputSizeMatchesWindowsExpectationOnX64()
    {
        if (!Environment.Is64BitProcess)
        {
            return;
        }

        Assert.Equal(40, Marshal.SizeOf<Win32InputNative.Input>());
    }
}
