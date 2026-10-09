using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class MinecraftExperimentalJarImportTests
{
    [Fact]
    public void ApprovedLocalJarReplacesExistingCausticaAndRestorePreservesOriginal()
    {
        var root = TempRoot();
        try
        {
            var mods = Path.Combine(root, "mods");
            var originalPath = Path.Combine(mods, "caustica-rtx-stable.jar");
            var originalBytes = Encoding.UTF8.GetBytes("existing managed build");
            File.WriteAllBytes(originalPath, originalBytes);
            var experimentalSource = Path.Combine(root, "selected-experiment.jar");
            MakeFakeJar(experimentalSource);
            var importer = new MinecraftExperimentalJarImportService(Sha256(experimentalSource));

            importer.Import(root, experimentalSource);
            Assert.True(importer.IsImported(root));
            Assert.False(File.Exists(originalPath));
            Assert.True(File.Exists(Path.Combine(mods, MinecraftExperimentalJarImportService.ExperimentalJarName)));
            Assert.True(new MinecraftRestirExperimentService().Inspect(root).Available);
            Assert.Throws<IOException>(() => importer.Import(root, experimentalSource));

            importer.Restore(root);
            Assert.False(importer.IsImported(root));
            Assert.Equal(originalBytes, File.ReadAllBytes(originalPath));
            Assert.False(File.Exists(Path.Combine(mods, MinecraftExperimentalJarImportService.ExperimentalJarName)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void WrongHashCannotOverwriteExistingCaustica()
    {
        var root = TempRoot();
        try
        {
            var originalPath = Path.Combine(root, "mods", "caustica-rtx-stable.jar");
            File.WriteAllText(originalPath, "original");
            var source = Path.Combine(root, "experiment.jar");
            MakeFakeJar(source);
            var importer = new MinecraftExperimentalJarImportService(new string('a', 64));

            Assert.Throws<InvalidDataException>(() => importer.Import(root, source));
            Assert.Equal("original", File.ReadAllText(originalPath));
            Assert.False(importer.IsImported(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TamperingAfterImportPreventsOverwritingModifiedJarOnRestore()
    {
        var root = TempRoot();
        try
        {
            var stable = Path.Combine(root, "mods", "caustica-stable.jar");
            File.WriteAllText(stable, "original");
            var source = Path.Combine(root, "input.jar");
            MakeFakeJar(source);
            var importer = new MinecraftExperimentalJarImportService(Sha256(source));
            importer.Import(root, source);
            File.AppendAllText(Path.Combine(root, "mods", MinecraftExperimentalJarImportService.ExperimentalJarName), "tampered");

            Assert.Throws<InvalidDataException>(() => importer.Restore(root));
            Assert.True(importer.IsImported(root));
            Assert.False(File.Exists(stable));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void InterruptedImportWithoutReceiptBlocksReinstallAndKeepsOriginalBackup()
    {
        var root = TempRoot();
        try
        {
            var originalPath = Path.Combine(root, "mods", "caustica-stable.jar");
            File.WriteAllText(originalPath, "previous stable jar");
            var backup = Path.Combine(root, ".dlss-nr-manager-backups", "caustica-experimental");
            Directory.CreateDirectory(backup);
            var backedUp = Path.Combine(backup, "original.jar");
            File.Move(originalPath, backedUp);

            var source = Path.Combine(root, "import.jar");
            MakeFakeJar(source);
            var importer = new MinecraftExperimentalJarImportService(Sha256(source));

            Assert.True(importer.IsImported(root));
            Assert.True(importer.HasInterruptedImport(root));
            Assert.False(importer.CanRestore(root));
            Assert.Throws<IOException>(() => importer.Import(root, source));
            Assert.Equal("previous stable jar", File.ReadAllText(backedUp));
            Assert.False(File.Exists(originalPath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void UnexpectedFilesInExperimentalBackupPreventRestoreWithoutDeletingUserFiles()
    {
        var root = TempRoot();
        try
        {
            var stable = Path.Combine(root, "mods", "caustica-stable.jar");
            File.WriteAllText(stable, "previous stable jar");
            var source = Path.Combine(root, "test-source.jar");
            MakeFakeJar(source);
            var importer = new MinecraftExperimentalJarImportService(Sha256(source));
            importer.Import(root, source);
            var backup = Path.Combine(root, ".dlss-nr-manager-backups", "caustica-experimental");
            var unrelatedFile = Path.Combine(backup, "my-notes.txt");
            File.WriteAllText(unrelatedFile, "preserve this file");

            Assert.True(importer.CanRestore(root));
            Assert.Throws<InvalidDataException>(() => importer.Restore(root));
            Assert.Equal("preserve this file", File.ReadAllText(unrelatedFile));
            Assert.False(File.Exists(stable));
            Assert.True(importer.IsImported(root));

            File.Delete(unrelatedFile);
            importer.Restore(root);
            Assert.False(importer.IsImported(root));
            Assert.Equal("previous stable jar", File.ReadAllText(stable));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void CorruptReceiptHidesAutomaticRestoreAndRetainsBothJars()
    {
        var root = TempRoot();
        try
        {
            var stable = Path.Combine(root, "mods", "caustica-stable.jar");
            File.WriteAllText(stable, "existing Caustica");
            var source = Path.Combine(root, "experiment.jar");
            MakeFakeJar(source);
            var importer = new MinecraftExperimentalJarImportService(Sha256(source));
            importer.Import(root, source);
            Assert.True(importer.CanRestore(root));

            var backup = Path.Combine(root, ".dlss-nr-manager-backups", "caustica-experimental");
            File.WriteAllText(Path.Combine(backup, "receipt.json"), "{corrupt");
            Assert.True(importer.IsImported(root));
            Assert.True(importer.HasInterruptedImport(root));
            Assert.False(importer.CanRestore(root));
            Assert.ThrowsAny<Exception>(() => importer.Restore(root));
            Assert.True(File.Exists(Path.Combine(backup, "original.jar")));
            Assert.True(File.Exists(Path.Combine(root, "mods", MinecraftExperimentalJarImportService.ExperimentalJarName)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ForgedSourceCommitInReceiptBlocksRestore()
    {
        var root = TempRoot();
        try
        {
            var stable = Path.Combine(root, "mods", "caustica-stable.jar");
            File.WriteAllText(stable, "existing Caustica");
            var source = Path.Combine(root, "experiment.jar");
            MakeFakeJar(source);
            var importer = new MinecraftExperimentalJarImportService(Sha256(source));
            importer.Import(root, source);

            var receiptPath = Path.Combine(root, ".dlss-nr-manager-backups", "caustica-experimental", "receipt.json");
            File.WriteAllText(receiptPath,
                File.ReadAllText(receiptPath).Replace(
                    MinecraftExperimentalJarImportService.BuildCommit,
                    new string('f', 40), StringComparison.Ordinal));
            Assert.False(importer.CanRestore(root));
            Assert.True(importer.HasInterruptedImport(root));
            Assert.Throws<InvalidDataException>(() => importer.Restore(root));
            Assert.Equal("existing Caustica",
                File.ReadAllText(Path.Combine(root, ".dlss-nr-manager-backups", "caustica-experimental", "original.jar")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "DlssNrManagerCausticaCI-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "mods"));
        return root;
    }

    private static string Sha256(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void MakeFakeJar(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "fabric.mod.json", "{\"id\":\"caustica\",\"version\":\"0.1.0-rtx.2\",\"depends\":{\"minecraft\":\"26.2\"}}");
        Add(zip, "dev/comfyfluffy/caustica/CausticaConfig$Rt$Lights.class", "lights.restir-di caustica.rt.restirDi");
        Add(zip, "caustica/natives/windows-x64/ngxshim.dll", new string('x', 2048));
        Add(zip, "caustica/natives/windows-x64/nvngx_dlssd.dll", new string('x', 2048));
        Add(zip, "caustica/natives/windows-x64/nvngx_dlssg.dll", new string('x', 2048));
        Add(zip, "padding.txt", new string('p', 4096));
    }

    private static void Add(ZipArchive zip, string path, string value)
    {
        using var stream = zip.CreateEntry(path, CompressionLevel.NoCompression).Open();
        var bytes = Encoding.UTF8.GetBytes(value);
        stream.Write(bytes);
    }
}
