using System.Reflection;

namespace DlssNrManager.Services;

public static class AppIdentity
{
    public static Version Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version
        ?? new Version(1, 0, 0);

    public static string VersionString =>
        $"{Version.Major}.{Version.Minor}.{Math.Max(0, Version.Build)}";

    public static string UserAgentVersion => VersionString;
}
