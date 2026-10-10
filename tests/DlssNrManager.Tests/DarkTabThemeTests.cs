using System.Xml.Linq;
using Xunit;

namespace DlssNrManager.Tests;

/// <summary>Protects all V4 pages against WPF's default light tab templates.</summary>
public sealed class DarkTabThemeTests
{
    private static readonly XNamespace Wpf =
        "http://schemas.microsoft.com/winfx/2006/xaml/2006/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    // WPF's actual presentation namespace (not the XAML language namespace).
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static XElement Load(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);
        Assert.True(File.Exists(path), $"WPF markup fixture missing: {name}");
        return XDocument.Load(path).Root!;
    }

    private static XElement ImplicitStyle(XElement resources, string type)
        => Assert.Single(resources.Descendants(Presentation + "Style")
            .Where(style => (string?)style.Attribute("TargetType") == type &&
                            style.Attribute(Xaml + "Key") is null));

    [Fact]
    public void Every_nested_tab_uses_global_dark_background_and_selected_content_template()
    {
        var main = Load("MainWindow.markup.xml");
        var all = main.Descendants(Presentation + "TabControl").ToArray();
        Assert.Equal(4, all.Length); // outer sidebar navigation, 2 nested pages, and remaining structure
        var nested = all.Where(tab => tab.Ancestors(Presentation + "TabControl").Any()).ToArray();
        Assert.Equal(2, nested.Length);
        var headings = nested.SelectMany(tab => tab.Elements(Presentation + "TabItem"))
            .Select(tab => (string?)tab.Attribute("Header")).ToArray();
        Assert.Equal(new[]
        {
            "VSR-HDR VLC (Direct)", "Restore HD Vidéo",
            "Créer", "Model Manager", "Jobs", "Runtime"
        }, headings);
        Assert.All(nested, tab =>
            Assert.Null(tab.Element(Presentation + "TabControl.Template")));
        Assert.NotNull(all.Single(tab => !tab.Ancestors(Presentation + "TabControl").Any())
            .Element(Presentation + "TabControl.Template"));

        var app = Load("App.markup.xml");
        var tabStyle = ImplicitStyle(app, "TabControl");
        var setters = tabStyle.Elements(Presentation + "Setter").ToArray();
        Assert.Contains(setters, element =>
            (string?)element.Attribute("Property") == "Background" &&
            (string?)element.Attribute("Value") == "{StaticResource AppBackground}");
        var template = Assert.Single(tabStyle.Descendants(Presentation + "ControlTemplate"));
        Assert.Equal("{x:Type TabControl}", (string?)template.Attribute("TargetType"));
        Assert.Contains(template.Descendants(Presentation + "TabPanel"),
            panel => (string?)panel.Attribute("IsItemsHost") == "True");
        Assert.Contains(template.Descendants(Presentation + "ContentPresenter"),
            presenter => (string?)presenter.Attribute("ContentSource") == "SelectedContent" &&
                         (string?)presenter.Attribute(Xaml + "Name") == "PART_SelectedContentHost");
        var contentPanel = Assert.Single(template.Descendants(Presentation + "Border"));
        Assert.Equal("{TemplateBinding Background}",
            (string?)contentPanel.Attribute("Background"));
        Assert.DoesNotContain(template.DescendantsAndSelf(),
            node => node.Attributes().Any(a =>
                a.Name.LocalName == "Background" &&
                (a.Value.Equals("White", StringComparison.OrdinalIgnoreCase) ||
                 a.Value.Equals("#FFFFFF", StringComparison.OrdinalIgnoreCase))));
    }

    [Fact]
    public void Tab_header_text_and_states_are_dark_themed_across_every_page()
    {
        var app = Load("App.markup.xml");
        var itemStyle = ImplicitStyle(app, "TabItem");
        var template = Assert.Single(itemStyle.Descendants(Presentation + "ControlTemplate"));
        Assert.Equal("{x:Type TabItem}", (string?)template.Attribute("TargetType"));
        var header = Assert.Single(template.Descendants(Presentation + "ContentPresenter"));
        Assert.Equal("Header", (string?)header.Attribute("ContentSource"));
        Assert.Equal("{TemplateBinding Foreground}",
            (string?)header.Attribute(
                XName.Get("Foreground", "http://schemas.microsoft.com/winfx/2006/xaml/presentation")));
        var triggers = template.Descendants(Presentation + "Trigger").ToArray();
        foreach (var pair in new[]
        {
            ("IsSelected", "True"),
            ("IsMouseOver", "True"),
            ("IsEnabled", "False")
        })
        {
            Assert.Contains(triggers,
                trigger => (string?)trigger.Attribute("Property") == pair.Item1 &&
                           (string?)trigger.Attribute("Value") == pair.Item2);
        }
        Assert.Contains(itemStyle.Elements(Presentation + "Setter"),
            setter => (string?)setter.Attribute("Property") == "Foreground" &&
                      (string?)setter.Attribute("Value") == "#B7C8D9");
        // No page may opt back in to WPF's light control defaults.
        var main = Load("MainWindow.markup.xml");
        Assert.DoesNotContain(main.Descendants(Presentation + "TabControl"),
            tab => tab.Attribute("Style") is not null);
    }
}
