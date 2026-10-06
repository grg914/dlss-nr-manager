using System.Text;

namespace DlssNrManager.Services;

public static class BinaryMarkerScanner
{
    private const int BufferSize = 256 * 1024;

    public static string? FindFirst(
        string path,
        IReadOnlyList<string> markers,
        long maxBytes = 64L * 1024 * 1024)
    {
        if (!File.Exists(path) || markers.Count == 0)
            return null;

        var valid = markers
            .Where(marker => !string.IsNullOrWhiteSpace(marker))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (valid.Length == 0)
            return null;

        var overlap = Math.Max(
            0,
            valid.Max(marker => Encoding.ASCII.GetByteCount(marker)) - 1);

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                BufferSize,
                FileOptions.SequentialScan);

            var buffer = new byte[BufferSize + overlap];
            var carry = 0;
            long consumed = 0;

            while (consumed < maxBytes)
            {
                var wanted = (int)Math.Min(
                    BufferSize,
                    maxBytes - consumed);

                var read = stream.Read(
                    buffer,
                    carry,
                    wanted);

                if (read == 0)
                    break;

                consumed += read;
                var length = carry + read;
                var text = Encoding.ASCII.GetString(
                    buffer,
                    0,
                    length);

                foreach (var marker in valid)
                {
                    if (text.Contains(
                            marker,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return marker;
                    }
                }

                carry = Math.Min(overlap, length);
                if (carry > 0)
                {
                    Buffer.BlockCopy(
                        buffer,
                        length - carry,
                        buffer,
                        0,
                        carry);
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
}
