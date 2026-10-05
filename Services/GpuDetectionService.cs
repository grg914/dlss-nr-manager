using Microsoft.Win32;
using DlssNrManager.Models;
namespace DlssNrManager.Services;
public sealed class GpuDetectionService
{
    public GpuInfo Detect()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
            if (key != null)
            {
                foreach (var sub in key.GetSubKeyNames())
                {
                    using var gpu = key.OpenSubKey(sub + @"\0000");
                    var name = gpu?.GetValue("DriverDesc") as string;
                    if (!string.IsNullOrWhiteSpace(name) && name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                        return Parse(name);
                }
            }
        } catch { }
        return new("Unknown GPU", "Unknown", false);
    }
    private static GpuInfo Parse(string name)
    {
        var gen = name.Contains("RTX 50", StringComparison.OrdinalIgnoreCase) ? "RTX 50" :
                  name.Contains("RTX 40", StringComparison.OrdinalIgnoreCase) ? "RTX 40" :
                  name.Contains("RTX 30", StringComparison.OrdinalIgnoreCase) ? "RTX 30" :
                  name.Contains("RTX 20", StringComparison.OrdinalIgnoreCase) ? "RTX 20" : "NVIDIA";
        return new(name, gen, true);
    }
}