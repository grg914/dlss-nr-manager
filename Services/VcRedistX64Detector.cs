using System.Security;
using Microsoft.Win32;

namespace DlssNrManager.Services;

public enum VcRedistX64State
{
    Installed,
    NotDetected,
    Unknown
}

public sealed record VcRedistX64Status(VcRedistX64State State, string? Version);

/// <summary>
/// Read-only host detection. A registered v14 x64 redistributable is not proof
/// that every native DLL (including OpenMP) is present or compatible.
/// </summary>
public static class VcRedistX64Detector
{
    private const string RegistryPath =
        @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64";

    public static VcRedistX64Status Detect()
    {
        if (!OperatingSystem.IsWindows())
            return new(VcRedistX64State.Unknown, null);

        var accessFailed = false;

        // Microsoft's installed-runtime key may be exposed in the 32-bit
        // registry view (Wow6432Node). Check both views explicitly.
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = machine.OpenSubKey(RegistryPath);
                if (key == null)
                    continue;

                var status = FromRegistryValues(
                    key.GetValue("Installed"),
                    key.GetValue("Version"));

                if (status.State == VcRedistX64State.Installed)
                    return status;
            }
            catch (Exception ex) when (ex is SecurityException
                                       or UnauthorizedAccessException
                                       or IOException)
            {
                accessFailed = true;
            }
        }

        return new(
            accessFailed ? VcRedistX64State.Unknown : VcRedistX64State.NotDetected,
            null);
    }

    // Pure mapping is also exercised by tests without modifying the host registry.
    public static VcRedistX64Status FromRegistryValues(
        object? installed,
        object? version)
    {
        if (installed is not int flag || flag != 1)
            return new(VcRedistX64State.NotDetected, null);

        var registeredVersion = (version as string)?.Trim();
        return new(
            VcRedistX64State.Installed,
            string.IsNullOrWhiteSpace(registeredVersion) ? null : registeredVersion);
    }
}
