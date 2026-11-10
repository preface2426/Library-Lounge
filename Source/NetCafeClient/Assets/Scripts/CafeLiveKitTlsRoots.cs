using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

// Keep certificate validation enabled when the native SDK cannot open the
// current-user Windows store from a Low integrity portable application.
public static class CafeLiveKitTlsRoots
{
    static bool prepared;
    static string certificateFile;
    static FileStream certificateStream;
    [StructLayout(LayoutKind.Sequential)]
    struct CertificateContext
    {
        public uint encoding;
        public IntPtr encoded;
        public uint encodedLength;
        public IntPtr info, store;
    }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CertOpenStore(IntPtr provider, uint encoding, IntPtr crypto, uint flags, string name);
    [DllImport("crypt32.dll")] static extern IntPtr CertEnumCertificatesInStore(IntPtr store, IntPtr previous);
    [DllImport("crypt32.dll")] static extern bool CertFreeCertificateContext(IntPtr certificate);
    [DllImport("crypt32.dll")] static extern bool CertCloseStore(IntPtr store, uint flags);
    [DllImport("crypt32.dll")] static extern int CertVerifyTimeValidity(IntPtr time, IntPtr info);
    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CertGetEnhancedKeyUsage(IntPtr certificate, uint flags, IntPtr usage, ref uint size);
    [DllImport("kernel32.dll")] static extern void SetLastError(uint error);

    static bool ServerAuthAllowed(IntPtr certificate)
    {
        uint size = 0;
        if (!CertGetEnhancedKeyUsage(certificate, 0, IntPtr.Zero, ref size) || size < 8 || size > 65536)
            return false;
        IntPtr usage = Marshal.AllocHGlobal((int)size);
        try
        {
            SetLastError(0);
            if (!CertGetEnhancedKeyUsage(certificate, 0, usage, ref size)) return false;
            int error = Marshal.GetLastWin32Error();
            int count = Marshal.ReadInt32(usage);
            if (count == 0) return error == unchecked((int)0x80092004); // all uses, not disabled
            if (count < 0 || count > 4096) return false;
            IntPtr oids = Marshal.ReadIntPtr(usage, IntPtr.Size); // native alignment on x86/x64
            for (int i = 0; i < count; ++i)
                if (Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(oids, i * IntPtr.Size)) == "1.3.6.1.5.5.7.3.1")
                    return true;
            return false;
        }
        finally { Marshal.FreeHGlobal(usage); }
    }

    public static bool Prepare()
    {
        if (prepared || !CafeWindowsLaunchContext.IsLowIntegrity()) return true;
        // Respect administrator/user supplied trust configuration.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSL_CERT_FILE")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSL_CERT_DIR"))) return true;
        try
        {
            var pem = new StringBuilder();
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            // The SDK opens the same merged current-user ROOT store with write
            // access. Low tokens can read this store when READONLY is explicit.
            IntPtr store = CertOpenStore(new IntPtr(10), 0, IntPtr.Zero, 0x1c000, "ROOT");
            if (store == IntPtr.Zero) return false;
            IntPtr certificate = IntPtr.Zero;
            try
            {
                while ((certificate = CertEnumCertificatesInStore(store, certificate)) != IntPtr.Zero)
                {
                    var context = Marshal.PtrToStructure<CertificateContext>(certificate);
                    // Match rustls-native-certs' Windows validity/EKU filtering,
                    // including Windows properties that a DER-only copy loses.
                    if (CertVerifyTimeValidity(IntPtr.Zero, context.info) != 0 ||
                        !ServerAuthAllowed(certificate)) continue;
                    if (context.encodedLength == 0 || context.encodedLength > 1024 * 1024) continue;
                    var bytes = new byte[context.encodedLength];
                    Marshal.Copy(context.encoded, bytes, 0, bytes.Length);
                    string der = Convert.ToBase64String(bytes);
                    if (!seen.Add(der)) continue;
                    pem.AppendLine("-----BEGIN CERTIFICATE-----");
                    for (int i = 0; i < der.Length; i += 64)
                        pem.AppendLine(der.Substring(i, Math.Min(64, der.Length - i)));
                    pem.AppendLine("-----END CERTIFICATE-----");
                }
            }
            finally
            {
                if (certificate != IntPtr.Zero) CertFreeCertificateContext(certificate);
                CertCloseStore(store, 0);
            }
            if (seen.Count == 0) return false;
            string directory = Path.Combine(Application.persistentDataPath, "LiveKitTls");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".pem");
            certificateStream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            certificateFile = path;
            using (var writer = new StreamWriter(certificateStream, new UTF8Encoding(false), 4096, true))
                writer.Write(pem.ToString());
            certificateStream.Flush();
            Environment.SetEnvironmentVariable("SSL_CERT_FILE", path);
            prepared = true;
            Application.quitting += CleanUp;
            Debug.Log("[NetCafe/TLS] Windows trusted roots prepared: " + seen.Count);
            return true;
        }
        catch (Exception error)
        {
            Debug.LogWarning("[NetCafe/TLS] Trusted root preparation failed: " + error.GetType().Name);
            CleanUp();
            return false;
        }
    }

    static void CleanUp()
    {
        if (certificateFile == null) return;
        if (Environment.GetEnvironmentVariable("SSL_CERT_FILE") == certificateFile)
            Environment.SetEnvironmentVariable("SSL_CERT_FILE", null);
        certificateStream?.Dispose();
        certificateStream = null;
        try { File.Delete(certificateFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        certificateFile = null;
        prepared = false;
    }
}
