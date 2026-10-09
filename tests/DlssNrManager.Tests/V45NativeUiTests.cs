using System.Xml.Linq;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class V45NativeUiTests
{
    [Fact]
    public void Quick_modes_use_existing_registered_tasks_without_a_second_execution_path()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "MainWindow.markup.xml");
        var document = XDocument.Load(file);
        var buttons = document.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Where(element => (string?)element.Attribute("Click") == "AiStudioQuickMode_Click")
            .ToArray();

        Assert.Equal(4, buttons.Length);
        var expected = new[]
        {
            AiStudioTaskKind.TextToImage, AiStudioTaskKind.ImageToImage,
            AiStudioTaskKind.TextToVideo, AiStudioTaskKind.VideoToVideo
        };
        foreach (var (button, task) in buttons.Zip(expected))
        {
            Assert.Equal(task.ToString(), (string?)button.Attribute("Tag"));
            Assert.Contains(LocalAiStudioService.TaskChoices, option => option.Task == task);
        }

        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        Assert.Single(document.Descendants()
            .Where(element => (string?)element.Attribute(x + "Name") == "AiStudioTaskBox"));
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Image" &&
            (string?)element.Attribute("Source") == "assets/branding/logo.png");
        Assert.Equal("assets/branding/app.ico",
            (string?)document.Root?.Attribute("Icon"));
    [Theory]
    [InlineData("Text-to-Image", "Texte vers image")]
    [InlineData("Image-to-Image", "Image vers image")]
    [InlineData("Text-to-Video", "Texte vers vidéo")]
    [InlineData("Video-to-Video", "Vidéo vers vidéo")]
    public void Quick_mode_buttons_are_bilingual(string english, string french)
    {
        Assert.Equal(french, UiLocalizationService.Translate(english, "fr"));
        Assert.Equal(english, UiLocalizationService.Translate(french, "en"));
    }

    }
}
