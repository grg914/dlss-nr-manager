using System.Reflection;
using System.Xml.Linq;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class V4SourceVersionTests
{
    [Fact]
    public void Assembly_identity_and_windows_version_match_v4_source_metadata()
    {
        var expected = new Version(4, 0, 0, 0);
        Assert.Equal(expected, AppIdentity.Version);
        Assert.Equal("4.0.0", AppIdentity.VersionString);
        Assert.Equal("4.0.0", AppIdentity.UserAgentVersion);
        var assembly = typeof(AppIdentity).Assembly;
        Assert.Equal(expected, assembly.GetName().Version);
        Assert.Equal("4.0.0",
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        var file = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
        Assert.Equal("4.0.0.0", file?.Version);
    }

    [Fact]
    public void Project_and_readme_do_not_misrepresent_release_as_signed_v4()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DlssNrManager.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var project = XDocument.Load(Path.Combine(dir!.FullName, "DlssNrManager.csproj"));
        foreach (var pair in new[]
        {
            ("Version", "4.0.0"),
            ("AssemblyVersion", "4.0.0.0"),
            ("FileVersion", "4.0.0.0"),
            ("InformationalVersion", "4.0.0")
        })
            Assert.Equal(pair.Item2, project.Descendants(pair.Item1).Single().Value);

        var readme = File.ReadAllText(Path.Combine(dir.FullName, "README.md"));
        Assert.Contains("V4 source version (candidate)", readme);
        Assert.Contains("v3.2.0", readme);
        Assert.Contains("not Authenticode-signed", readme);
    }
}
