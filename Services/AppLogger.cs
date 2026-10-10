using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace DlssNrManager.Services;

public static class AppLogger
{
    private static readonly object Gate = new();
    private const long MaxLogBytes = 5L * 1024L * 1024L;

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "logs");

    public static string LogPath { get; } =
        Path.Combine(LogDirectory, "dlss-nr-manager.log");

    public static string PreviousLogPath { get; } =
        Path.Combine(LogDirectory, "dlss-nr-manager.previous.log");

    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            RotateIfNeeded();

            Info("Application session started.");
            Info($"Version: {GetVersion()}");
            Info($"Executable: {Environment.ProcessPath ?? "unknown"}");
            Info($"OS: {RuntimeInformation.OSDescription}");
            Info($"Framework: {RuntimeInformation.FrameworkDescription}");
            Info($"Architecture: process={RuntimeInformation.ProcessArchitecture}, OS={RuntimeInformation.OSArchitecture}");
            Info($"User interactive: {Environment.UserInteractive}");
        }
        catch
        {
            // Logging must never prevent the application from starting.
        }
    }

    public static void Trace(string message)
        => Write("TRACE", message, null);

    public static void Debug(string message)
        => Write("DEBUG", message, null);

    public static void Info(string message)
        => Write("INFO", message, null);

    public static void Warn(string message)
        => Write("WARNING", message, null);

    public static void Warning(string message)
        => Warn(message);

    public static void Error(string message, Exception? exception = null)
        => Write("ERROR", message, exception);

    public static void Critical(string message, Exception? exception = null)
        => Write("CRITICAL", message, exception);

    // Never include raw tokens/passwords in a diagnostic export. Redaction
    // covers common structured headers, query parameters and GitHub PATs.
    public static string RedactSensitiveData(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var options = System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                      System.Text.RegularExpressions.RegexOptions.CultureInvariant;
        var timeout = TimeSpan.FromMilliseconds(250);
        try
        {
            value = System.Text.RegularExpressions.Regex.Replace(
                value,
                @"(\bAuthorization\s*[:=]\s*(?:Bearer|Basic|Token)\s+)[^\s,;]+",
                "$1[REDACTED]", options, timeout);
            value = System.Text.RegularExpressions.Regex.Replace(
                value,
                @"(\b(?:api[_-]?key|access[_-]?token|refresh[_-]?token|client[_-]?secret|password|passwd|secret|token)\b""?\s*[:=]\s*)(?:""[^""]*""|'[^']*'|[^\s&,;}\]]+)",
                "$1[REDACTED]", options, timeout);
            value = System.Text.RegularExpressions.Regex.Replace(
                value,
                @"\b(?:gh[pousr]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,})\b",
                "[REDACTED]", options, timeout);
            return value;
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            // A maliciously expensive error string must never go to disk.
            return "[REDACTED: log message exceeded safe inspection budget]";
        }
    }

    public static IDisposable Scope(string operation)
    {
        var stopwatch = Stopwatch.StartNew();
        Info($"BEGIN {operation}");
        return new ScopeHandle(operation, stopwatch);
    }

    public static string Tail(int maxChars = 12000)
    {
        try
        {
            lock (Gate)
            {
                if (!File.Exists(LogPath))
                    return "";

                var text = File.ReadAllText(LogPath, Encoding.UTF8);
                return text.Length <= maxChars
                    ? text
                    : text[^maxChars..];
            }
        }
        catch
        {
            return "";
        }
    }

    private static void Write(
        string level,
        string message,
        Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded();

                var builder = new StringBuilder();
                builder.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
                builder.Append(" [");
                builder.Append(level);
                builder.Append("] [PID ");
                builder.Append(Environment.ProcessId);
                builder.Append("] ");
                builder.AppendLine(message);

                if (exception != null)
                {
                    builder.AppendLine(
                        $"Exception: {exception.GetType().FullName}: {exception.Message}");
                    builder.AppendLine(exception.StackTrace ?? "(no stack trace)");

                    var inner = exception.InnerException;
                    var depth = 0;
                    while (inner != null && depth < 8)
                    {
                        builder.AppendLine(
                            $"Inner[{depth}]: {inner.GetType().FullName}: {inner.Message}");
                        builder.AppendLine(inner.StackTrace ?? "(no stack trace)");
                        inner = inner.InnerException;
                        depth++;
                    }
                }

                File.AppendAllText(
                    LogPath,
                    Sanitize(builder.ToString()),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch
        {
            // Never throw from logging.
        }
    }

    private static string Sanitize(string value)
    {
        try
        {
            var replacements = new[]
            {
                (
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.UserProfile),
                    "%USERPROFILE%"),
                (
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "%LOCALAPPDATA%"),
                (
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.ApplicationData),
                    "%APPDATA%")
            };

            foreach (var (path, token) in replacements
                         .Where(item => !string.IsNullOrWhiteSpace(item.Item1))
                         .OrderByDescending(item => item.Item1.Length))
            {
                value = value.Replace(
                    path,
                    token,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Sanitization is best-effort.
        }

        return RedactSensitiveData(value);
    }

    private static void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(LogPath))
                return;

            if (new FileInfo(LogPath).Length < MaxLogBytes)
                return;

            TryDelete(PreviousLogPath);
            File.Move(LogPath, PreviousLogPath, true);
        }
        catch
        {
            // Best-effort rotation.
        }
    }

    private static string GetVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
            return informational;

        return assembly.GetName().Version?.ToString() ?? "unknown";
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

    private sealed class ScopeHandle(
        string operation,
        Stopwatch stopwatch) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            stopwatch.Stop();
            Info($"END {operation} ({stopwatch.Elapsed.TotalSeconds:F2}s)");
        }
    }
}
