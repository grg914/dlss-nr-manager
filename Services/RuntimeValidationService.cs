using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using DlssNrManager.Models;

namespace DlssNrManager.Services;

public static class RuntimeValidationService
{
    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static async Task<RuntimeValidation> ValidateAsync(string path, string gpuGeneration)
    {
        var hash = await HashService.Sha256Async(path);
        var expected = gpuGeneration == "RTX 50"
            ? InstallerService.Rtx50Hash
            : gpuGeneration is "RTX 20" or "RTX 30" or "RTX 40"
                ? InstallerService.Rtx2040Hash
                : null;

        var hashValid = expected != null && hash.Equals(expected, StringComparison.OrdinalIgnoreCase);
        var signatureValid = VerifyAuthenticode(path);
        var publisher = TryGetPublisher(path);
        var version = TryGetFileVersion(path);
        var is64Bit = IsPe64(path);

        return new(hash, hashValid, signatureValid, publisher, version, is64Bit);
    }

    private static string? TryGetPublisher(string path)
    {
        try
        {
            var cert = X509Certificate.CreateFromSignedFile(path);
            using var cert2 = new X509Certificate2(cert);
            return cert2.GetNameInfo(X509NameType.SimpleName, false);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetFileVersion(string path)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(path).FileVersion;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsPe64(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            return pe.PEHeaders.PEHeader?.Magic == PEMagic.PE32Plus;
        }
        catch
        {
            return false;
        }
    }

    private static bool VerifyAuthenticode(string path)
    {
        var fileInfo = new WinTrustFileInfo(path);
        var data = new WinTrustData(fileInfo);

        try
        {
            return WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, ref data) == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            data.Dispose();
            fileInfo.Dispose();
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref WinTrustData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustFileInfo : IDisposable
    {
        private uint cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>();
        private IntPtr pcwszFilePath;
        private IntPtr hFile = IntPtr.Zero;
        private IntPtr pgKnownSubject = IntPtr.Zero;

        public WinTrustFileInfo(string path)
        {
            pcwszFilePath = Marshal.StringToCoTaskMemUni(path);
        }

        public void Dispose()
        {
            if (pcwszFilePath != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pcwszFilePath);
                pcwszFilePath = IntPtr.Zero;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustData : IDisposable
    {
        private uint cbStruct = (uint)Marshal.SizeOf<WinTrustData>();
        private IntPtr pPolicyCallbackData = IntPtr.Zero;
        private IntPtr pSIPClientData = IntPtr.Zero;
        private uint dwUIChoice = 2; // WTD_UI_NONE
        private uint fdwRevocationChecks = 0;
        private uint dwUnionChoice = 1; // WTD_CHOICE_FILE
        private IntPtr pFile;
        private uint dwStateAction = 0;
        private IntPtr hWVTStateData = IntPtr.Zero;
        private IntPtr pwszURLReference = IntPtr.Zero;
        private uint dwProvFlags = 0x00000010; // WTD_REVOCATION_CHECK_NONE
        private uint dwUIContext = 0;

        public WinTrustData(WinTrustFileInfo fileInfo)
        {
            pFile = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, pFile, false);
        }

        public void Dispose()
        {
            if (pFile != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pFile);
                pFile = IntPtr.Zero;
            }
        }
    }
}
