using System;
using System.Runtime.InteropServices;

// Read only: never relabel files, change ACLs, elevate, or alter the caller's token.
public static class CafeWindowsLaunchContext
{
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool GetTokenInformation(IntPtr token, int info, IntPtr data, uint size, out uint needed);
    [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);

    public static bool IsLowIntegrity()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) return false;
        if (!OpenProcessToken(GetCurrentProcess(), 8, out var token)) return false;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            GetTokenInformation(token, 25, IntPtr.Zero, 0, out uint needed);
            if (needed == 0 || needed > 65536) return false;
            buffer = Marshal.AllocHGlobal((int)needed);
            if (!GetTokenInformation(token, 25, buffer, needed, out _)) return false;
            IntPtr sid = Marshal.ReadIntPtr(buffer);
            if (sid == IntPtr.Zero) return false;
            byte count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            return count > 0 && (uint)Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)count - 1)) < 8192;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            CloseHandle(token);
        }
    }
}
