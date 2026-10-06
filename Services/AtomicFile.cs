using System.Text;

namespace DlssNrManager.Services;

public static class AtomicFile
{
    public static void WriteAllText(
        string path,
        string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            File.WriteAllText(
                temp,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    public static void WriteAllLines(
        string path,
        IEnumerable<string> lines)
        => WriteAllText(
            path,
            string.Join(
                Environment.NewLine,
                lines) + Environment.NewLine);

    public static void WriteAllBytes(
        string path,
        ReadOnlySpan<byte> bytes)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            File.WriteAllBytes(temp, bytes.ToArray());
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }
}
