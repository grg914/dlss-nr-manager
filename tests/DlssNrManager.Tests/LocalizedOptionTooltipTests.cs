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

    [Theory]
    [InlineData("Jamais sortir en HDR", "Never output HDR")]
    [InlineData("Versions officielles des outils", "Official tool versions")]
    [InlineData("Comparer les dépôts officiels aux sources suivies (sans installation automatique).", "Compare official repositories against tracked sources (without automatic installation).")]
    [InlineData("Vérifier versions officielles", "Check official versions")]
    [InlineData("Voir le dépôt officiel", "View official repository")]
    [InlineData("Contrôle à la demande uniquement.", "On-demand check only.")]
    [InlineData("GAME", "JEU")]
    public void French_first_static_labels_translate_both_ways(
        string french, string english)
    {
        Assert.Equal(english, UiLocalizationService.Translate(french, "en"));
        Assert.Equal(french, UiLocalizationService.Translate(english, "fr"));
    }

    [Fact]
    public void French_first_labels_are_present_in_the_Wpf_markup()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "MainWindow.markup.xml");
        var document = XDocument.Load(path);
        var attributes = document.Descendants()
            .SelectMany(node => node.Attributes())
            .Where(attribute => attribute.Name.LocalName is "Text" or "Content")
            .Select(attribute => attribute.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var french in new[]
        {
            "Jamais sortir en HDR",
            "Versions officielles des outils",
            "Vérifier versions officielles",
            "Voir le dépôt officiel",
            "Contrôle à la demande uniquement."
        })
        {
            Assert.Contains(french, attributes);
            Assert.NotEqual(french, UiLocalizationService.Translate(french, "en"));
        }
    }
}
