using System.IO.Compression;
using System.Text;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class MinecraftRestirExperimentTests
{
    [Fact]
    public void StableJarCannotEnableRestir()
    {
        var root = NewInstance("caustica-rtx.jar", supported: false);
        try
        {
            var service = new MinecraftRestirExperimentService();
            Assert.False(service.Inspect(root).Available);
            Assert.Throws<InvalidOperationException>(() => service.SetEnabled(root, true));
            Assert.False(File.Exists(Path.Combine(root, "config", "caustica.toml")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ExperimentalJarAllowsExplicitToggleAndRollback()
    {
        var root = NewInstance("Caustica-RTX-test.jar", supported: true);
        try
        {
            var service = new MinecraftRestirExperimentService();
            Assert.True(service.Inspect(root).Available);
            Assert.False(service.Inspect(root).Enabled);

            service.SetEnabled(root, true);
            Assert.True(service.Inspect(root).Enabled);
            service.SetEnabled(root, false);
            Assert.False(service.Inspect(root).Enabled);

            var config = Path.Combine(root, "config", "caustica.toml");
            Assert.Contains("restir-di = false", File.ReadAllText(config));
            Assert.True(File.Exists(config + ".restir-backup"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ExistingTomlPreservesUnrelatedSettingsAndComments()
    {
        var root = NewInstance("caustica-rtx.jar", supported: true);
        try
        {
            var config = Path.Combine(root, "config", "caustica.toml");
            Directory.CreateDirectory(Path.GetDirectoryName(config)!);
            File.WriteAllText(config, "[enabled]\r\nfoo = true\r\n\r\n[lights]\r\nris-candidates = 8\r\nrestir-di = false # user choice\r\n\r\n[tonemap]\r\ngamma = 1\r\n");
            var service = new MinecraftRestirExperimentService();
            service.SetEnabled(root, true);

            var changed = File.ReadAllText(config);
            Assert.Contains("ris-candidates = 8", changed);
            Assert.Contains("restir-di = true # user choice\r\n", changed);
            Assert.Contains("[tonemap]\r\ngamma = 1", changed);
            Assert.DoesNotContain("\nrestir-di = false", changed);
            Assert.Equal(1, changed.Split("restir-di =", StringSplitOptions.None).Length - 1);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void MalformedOrDuplicatedSettingFailsClosed()
    {
        var root = NewInstance("caustica-rtx.jar", supported: true);
        try
        {
            var config = Path.Combine(root, "config", "caustica.toml");
            Directory.CreateDirectory(Path.GetDirectoryName(config)!);
            const string unsafeText = "[lights]\nrestir-di = maybe\n";
            File.WriteAllText(config, unsafeText);
            var service = new MinecraftRestirExperimentService();
            Assert.False(service.Inspect(root).Available);
            Assert.Throws<InvalidOperationException>(() => service.SetEnabled(root, true));
            Assert.Equal(unsafeText, File.ReadAllText(config));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void WrongMinecraftVersionIsRejectedEvenIfTheFeatureMarkerExists()
    {
        var root = NewInstance("Caustica-RTX-test.jar", supported: true, minecraftVersion: "1.20.1");
        try
        {
            var service = new MinecraftRestirExperimentService();
            Assert.False(service.Inspect(root).Available);
            Assert.Throws<InvalidOperationException>(() => service.SetEnabled(root, true));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string NewInstance(string jarName, bool supported, string minecraftVersion = "26.2")
    {
        var root = Path.Combine(Path.GetTempPath(), "DlssNrRestir-" + Guid.NewGuid().ToString("N"));
        var mods = Directory.CreateDirectory(Path.Combine(root, "mods")).FullName;
        using (var archive = ZipFile.Open(Path.Combine(mods, jarName), ZipArchiveMode.Create))
        {
            var metadata = archive.CreateEntry("fabric.mod.json");
            using (var writer = new StreamWriter(metadata.Open(), new UTF8Encoding(false)))
                writer.Write("{\"id\":\"caustica\",\"depends\":{\"minecraft\":\"" + minecraftVersion + "\"}}");
            var entry = archive.CreateEntry("dev/comfyfluffy/caustica/CausticaConfig$Rt$Lights.class");
            using var stream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(supported
                ? "lights.restir-di caustica.rt.restirDi"
                : "lights.ris-candidates caustica.rt.risCandidates");
            stream.Write(bytes);
        }
        return root;
    }
}
