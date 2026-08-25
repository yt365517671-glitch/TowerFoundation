using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TowerFoundation.Licensing;

internal static class WindowsDataProtector
{
    private const uint CryptProtectUiForbidden = 0x1;
    private static readonly byte[] OptionalEntropy =
        Encoding.UTF8.GetBytes("TowerFoundation/LicenseStorage/V1");

    public static byte[] Protect(ReadOnlySpan<byte> plaintext) => Transform(plaintext, true);

    public static byte[] Unprotect(ReadOnlySpan<byte> ciphertext) => Transform(ciphertext, false);

    private static byte[] Transform(ReadOnlySpan<byte> input, bool protect)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("授权私钥保护仅支持 Windows。");
        }
        var inputBytes = input.ToArray();
        var inputBlob = CreateBlob(inputBytes);
        var entropyBlob = CreateBlob(OptionalEntropy);
        try
        {
            var succeeded = protect
                ? CryptProtectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero,
                    IntPtr.Zero, CryptProtectUiForbidden, out var outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero,
                    IntPtr.Zero, CryptProtectUiForbidden, out outputBlob);
            if (!succeeded)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    protect ? "无法使用 Windows DPAPI 加密授权数据。" : "无法使用 Windows DPAPI 解密授权数据。");
            }
            try
            {
                var output = new byte[outputBlob.Size];
                Marshal.Copy(outputBlob.Data, output, 0, output.Length);
                return output;
            }
            finally
            {
                if (outputBlob.Data != IntPtr.Zero)
                {
                    _ = LocalFree(outputBlob.Data);
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(inputBytes);
            FreeBlob(inputBlob);
            FreeBlob(entropyBlob);
        }
    }

    private static DataBlob CreateBlob(ReadOnlySpan<byte> value)
    {
        if (value.Length == 0) return default;
        var pointer = Marshal.AllocHGlobal(value.Length);
        var bytes = value.ToArray();
        try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        return new DataBlob { Size = value.Length, Data = pointer };
    }

    private static void FreeBlob(DataBlob blob)
    {
        if (blob.Data == IntPtr.Zero) return;
        Marshal.Copy(new byte[blob.Size], 0, blob.Data, blob.Size);
        Marshal.FreeHGlobal(blob.Data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Size; public IntPtr Data; }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description,
        ref DataBlob optionalEntropy, IntPtr reserved, IntPtr promptStruct, uint flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description,
        ref DataBlob optionalEntropy, IntPtr reserved, IntPtr promptStruct, uint flags, out DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
