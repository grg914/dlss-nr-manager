using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class FinalV4LocalizationAndCrashTests
{
    [Theory]
    [InlineData("AI Upscale / Real-ESRGAN", "Agrandissement IA / Real-ESRGAN")]
    [InlineData("Anime / Illustration", "Animé / Illustration")]
    [InlineData("Microsoft Visual C++ Redistributable x64: détection locale…", "Microsoft Visual C++ Redistributable x64 : détection locale…")]
    public void Remaining_v4_labels_round_trip_between_languages(string english, string french)
    {
        Assert.Equal(french, UiLocalizationService.Translate(english, "fr"));
        Assert.Equal(english, UiLocalizationService.Translate(french, "en"));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    public void Fatal_dialog_shows_cause_module_solution_and_local_log(string language)
    {
        var text = ApplicationFailureReport.Format(
            new DllNotFoundException("SensitiveDll.dll token=topSecretString"),
            "TestModule",
            language,
            "%LOCALAPPDATA%\\DlssNrManager\\logs\\dlss-nr-manager.log");
        Assert.Contains("TestModule", text);
        Assert.Contains("dlss-nr-manager.log", text);
        Assert.DoesNotContain("topSecretString", text);
        Assert.Contains(language == "fr" ? "Cause probable" : "Probable cause", text);
        Assert.Contains(language == "fr" ? "Solution" : "Solution", text);
    }

    [Fact]
    public void Fatal_dialog_does_not_hide_unhandled_dispatcher_exceptions()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null &&
               !File.Exists(Path.Combine(root.FullName, "DlssNrManager.csproj")))
            root = root.Parent;
        Assert.NotNull(root);
        var app = File.ReadAllText(Path.Combine(root!.FullName, "App.xaml.cs"));
        Assert.Contains("ApplicationFailureReport.Format(", app);
        Assert.Contains("e.Handled = false;", app);
        Assert.Contains("ExternalProcessTracker.KillAll();", app);
    }
}
