using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LocalAsrClient.Core.Abstractions;

namespace LocalAsrClient.App.Security;

public sealed class DpapiSecretProtector : ISecretProtector
{
    private const uint CryptProtectUiForbidden = 0x1;

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            return Convert.ToBase64String(ProtectBytes(plaintextBytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        byte[] protectedBytes;
        try
        {
            protectedBytes = Convert.FromBase64String(protectedValue);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("保存的 API Key 无法解析，请重新输入。", ex);
        }

        byte[]? plaintextBytes = null;
        try
        {
            plaintextBytes = UnprotectBytes(protectedBytes);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (Exception ex) when (ex is Win32Exception or CryptographicException)
        {
            throw new InvalidOperationException(
                "保存的 API Key 无法用当前 Windows 用户解密，请重新输入。",
                ex);
        }
        finally
        {
            if (plaintextBytes is not null)
            {
                CryptographicOperations.ZeroMemory(plaintextBytes);
            }

            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private static byte[] ProtectBytes(byte[] plaintext)
    {
        return Transform(
            plaintext,
            static (ref DataBlob input, out DataBlob output) =>
                CryptProtectData(
                    ref input,
                    null,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output));
    }

    private static byte[] UnprotectBytes(byte[] protectedValue)
    {
        return Transform(
            protectedValue,
            static (ref DataBlob input, out DataBlob output) =>
            {
                var succeeded = CryptUnprotectData(
                    ref input,
                    out var description,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output);
                if (description != IntPtr.Zero)
                {
                    _ = LocalFree(description);
                }

                return succeeded;
            });
    }

    private static byte[] Transform(byte[] inputBytes, DpapiTransform transform)
    {
        var inputPointer = Marshal.AllocHGlobal(inputBytes.Length);
        try
        {
            Marshal.Copy(inputBytes, 0, inputPointer, inputBytes.Length);
            var input = new DataBlob(inputBytes.Length, inputPointer);
            if (!transform(ref input, out var output))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows DPAPI 操作失败。");
            }

            try
            {
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally
            {
                if (output.Data != IntPtr.Zero)
                {
                    _ = LocalFree(output.Data);
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(inputBytes);
            Marshal.FreeHGlobal(inputPointer);
        }
    }

    private delegate bool DpapiTransform(ref DataBlob input, out DataBlob output);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DataBlob
    {
        public DataBlob(int size, IntPtr data)
        {
            Size = size;
            Data = data;
        }

        public int Size { get; }

        public IntPtr Data { get; }
    }

    [DllImport("Crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? dataDescription,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStructure,
        uint flags,
        out DataBlob dataOut);

    [DllImport("Crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        out IntPtr dataDescription,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStructure,
        uint flags,
        out DataBlob dataOut);

    [DllImport("Kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
