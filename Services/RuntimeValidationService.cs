using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using DlssNrManager.Models;

namespace DlssNrManager.Services;

public static class RuntimeValidationService
{
    private static readonly Guid WintrustActionGenericVerifyV2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static async Task<RuntimeValidation> ValidateAsync(
        string path,
        string gpuGeneration,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Runtime DLL was not found.", path);

        var hash = await HashService.Sha256Async(path, cancellationToken);
        var expected = gpuGeneration == "RTX 50"
            ? InstallerService.Rtx50Hash
            : gpuGeneration is "RTX 20" or "RTX 30" or "RTX 40"
                ? InstallerService.Rtx2040Hash
                : null;

        var hashValid = expected != null &&
                        hash.Equals(expected, StringComparison.OrdinalIgnoreCase);

        return new RuntimeValidation(
            hash,
            hashValid,
            VerifyAuthenticode(path),
            TryGetPublisher(path),
            TryGetFileVersion(path),
            IsPe64(path));
    }

    private static string? TryGetPublisher(string path)
    {
        try
        {
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            return cert.GetNameInfo(X509NameType.SimpleName, false);
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
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
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
        IntPtr pathPtr = IntPtr.Zero;
        IntPtr fileInfoPtr = IntPtr.Zero;
        var action = WintrustActionGenericVerifyV2;
        var data = new WinTrustData();

        try
        {
            pathPtr = Marshal.StringToCoTaskMemUni(path);

            var fileInfo = new WinTrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = pathPtr,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };

            fileInfoPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = 2,             // WTD_UI_NONE
                fdwRevocationChecks = 0,    // WTD_REVOKE_NONE
                dwUnionChoice = 1,          // WTD_CHOICE_FILE
                pFile = fileInfoPtr,
                dwStateAction = 1,          // WTD_STATEACTION_VERIFY
                dwProvFlags = 0x00000010,   // WTD_REVOCATION_CHECK_NONE
                dwUIContext = 0
            };

            return WinVerifyTrust(IntPtr.Zero, ref action, ref data) == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (fileInfoPtr != IntPtr.Zero)
            {
                try
                {
                    data.dwStateAction = 2; // WTD_STATEACTION_CLOSE
                    WinVerifyTrust(IntPtr.Zero, ref action, ref data);
                }
                catch { }

                Marshal.FreeCoTaskMem(fileInfoPtr);
            }

            if (pathPtr != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pathPtr);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(
        IntPtr hwnd,
        ref Guid pgActionId,
        ref WinTrustData pWvtData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
    }
}
