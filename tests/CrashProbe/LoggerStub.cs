namespace DlssNrManager.Services;

// The crash harness compiles the identical production ExternalProcessTracker
// source, without depending on the self-contained WPF executable. Logging is
// the only production dependency, so warnings are written to stderr here.
internal static class AppLogger
{
    public static void Warn(string message) => Console.Error.WriteLine(message);
}
