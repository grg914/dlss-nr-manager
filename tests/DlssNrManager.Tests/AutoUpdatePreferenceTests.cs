using System.Text.Json;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class AutoUpdatePreferenceTests
{
    [Fact]
    public void Old_preferences_without_auto_update_field_default_to_disabled()
    {
        var legacy = JsonSerializer.Deserialize<AppPreferences>(
            """{"SoftwareRendering":false,"Language":"fr"}""");

        Assert.NotNull(legacy);
        Assert.False(legacy.AutoUpdateComponents);
        Assert.Equal("fr", legacy.Language);
    }

    [Fact]
    public void Enabled_opt_in_survives_serialization_and_language_change()
    {
        var original = new AppPreferences(false, "en", AutoUpdateComponents: true);
        var persisted = JsonSerializer.Deserialize<AppPreferences>(
            JsonSerializer.Serialize(original));

        Assert.NotNull(persisted);
        Assert.True(persisted.AutoUpdateComponents);
        Assert.True((persisted with { Language = "fr" }).AutoUpdateComponents);
    }

    [Fact]
    public void Auto_update_is_optional_for_new_preferences()
    {
        Assert.False(new AppPreferences(false, "en").AutoUpdateComponents);
    }
}
