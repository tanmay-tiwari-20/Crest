using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Crest.Features;

public interface IClipboardHistoryProtection
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] protectedData);
}

/// <summary>Windows DPAPI binds retained clipboard data to the current Windows user.</summary>
internal sealed class WindowsClipboardHistoryProtection : IClipboardHistoryProtection
{
    public byte[] Protect(byte[] plaintext) => WindowsUserDataProtection.Protect(plaintext);
    public byte[] Unprotect(byte[] protectedData) => WindowsUserDataProtection.Unprotect(protectedData);
}

internal static class WindowsUserDataProtection
{
    public static byte[] Protect(byte[] plaintext) => Transform(plaintext, protect: true);
    public static byte[] Unprotect(byte[] protectedData) => Transform(protectedData, protect: false);

    private static byte[] Transform(byte[] value, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var inputHandle = GCHandle.Alloc(value, GCHandleType.Pinned);
        var input = new DataBlob { Length = value.Length, Data = inputHandle.AddrOfPinnedObject() };
        DataBlob output = default;
        try
        {
            const uint noUserInterface = 1;
            bool success = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, noUserInterface, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, noUserInterface, out output);
            if (!success) throw new CryptographicException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
            if (output.Length is < 1 or > 1024 * 1024) throw new CryptographicException("Invalid protected data length.");
            byte[] result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (output.Data != IntPtr.Zero)
            {
                if (!protect)
                    for (int index = 0; index < output.Length; index++) Marshal.WriteByte(output.Data, index, 0);
                LocalFree(output.Data);
            }
            inputHandle.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
