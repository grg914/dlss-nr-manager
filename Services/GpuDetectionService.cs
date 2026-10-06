using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using DlssNrManager.Models;

namespace DlssNrManager.Services;

public sealed class GpuDetectionService
{
    public GpuInfo Detect()
    {
        var names = DetectRegistryNames()
            .Concat(DetectViaNvidiaSmi())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var candidates = names
            .Select(Parse)
            .Where(x => x.IsNvidia)
            .OrderByDescending(x => GenerationRank(x.Generation))
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return candidates.FirstOrDefault()
               ?? new("Unknown GPU", "Unknown", false);
    }

    private static IEnumerable<string> DetectRegistryNames()
    {
        RegistryKey? key = null;
        try
        {
            key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
            if (key == null)
                yield break;

            foreach (var sub in key.GetSubKeyNames())
            {
                string? name = null;
                try
                {
                    using var gpu = key.OpenSubKey(sub + @"\0000");
                    name = gpu?.GetValue("DriverDesc") as string;
                }
                catch { }

                if (!string.IsNullOrWhiteSpace(name) &&
                    name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    yield return name;
            }
        }
        finally
        {
            key?.Dispose();
        }
    }

    private static IEnumerable<string> DetectViaNvidiaSmi()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "nvidia-smi.exe",
                Arguments = "--query-gpu=name --format=csv,noheader",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process == null)
                return [];

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000) || process.ExitCode != 0)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch { }

                return [];
            }

            return output
                .Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);
        }
        catch
        {
            return [];
        }
    }

    private static GpuInfo Parse(string name)
    {
        var normalized = Regex.Replace(name, @"\s+", " ").Trim();

        var gen = Regex.IsMatch(normalized, @"\bRTX\s*50\d{2}\b", RegexOptions.IgnoreCase) ? "RTX 50" :
                  Regex.IsMatch(normalized, @"\bRTX\s*40\d{2}\b", RegexOptions.IgnoreCase) ? "RTX 40" :
                  Regex.IsMatch(normalized, @"\bRTX\s*30\d{2}\b", RegexOptions.IgnoreCase) ? "RTX 30" :
                  Regex.IsMatch(normalized, @"\bRTX\s*20\d{2}\b", RegexOptions.IgnoreCase) ? "RTX 20" :
                  "NVIDIA";

        return new(normalized, gen, true);
    }

    private static int GenerationRank(string generation)
        => generation switch
        {
            "RTX 50" => 50,
            "RTX 40" => 40,
            "RTX 30" => 30,
            "RTX 20" => 20,
            "NVIDIA" => 1,
            _ => 0
        };
}
