using System.Xml.Linq;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class LocalizedOptionTooltipTests
{
    [Fact]
    public void Every_named_option_has_a_short_bilingual_tooltip()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "MainWindow.markup.xml");
        Assert.True(File.Exists(path), "WPF XAML fixture must be copied to the test output.");

        var document = XDocument.Load(path);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var controls = document.Descendants()
            .Where(node => node.Name.LocalName is "CheckBox" or "ComboBox" or "Slider")
            .Where(node => node.Attribute(xaml + "Name") != null)
            .ToArray();

        Assert.True(controls.Length >= 50, "Unexpected loss of named WPF options.");
        foreach (var control in controls)
        {
            var name = (string)control.Attribute(xaml + "Name")!;
            var tooltip = (string?)control.Attribute("ToolTip");
            Assert.False(string.IsNullOrWhiteSpace(tooltip),
                $"{name}: missing explanatory tooltip.");
            Assert.True(tooltip!.Length <= 240,
                $"{name}: tooltip should be concise.");
            var french = UiLocalizationService.Translate(tooltip, "fr");
            Assert.NotEqual(tooltip, french);
            Assert.Equal(tooltip, UiLocalizationService.Translate(french, "en"));
        }
    }

    [Fact]
    public void Both_languages_preserve_the_application_language_selector()
    {
        Assert.Equal("fr", UiLocalizationService.NormalizeLanguage("fr-CH"));
        Assert.Equal("en", UiLocalizationService.NormalizeLanguage("en-US"));
        Assert.Equal("en", UiLocalizationService.NormalizeLanguage("de-CH"));
    }
}
