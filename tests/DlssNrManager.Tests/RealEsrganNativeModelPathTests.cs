using Xunit;

namespace DlssNrManager.Tests;

public sealed class RealEsrganNativeModelPathTests
{
    [Fact]
    public void Native_anime_model_paths_use_platform_width_strings_without_printf_varargs()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "DlssNrManager.csproj")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(
            directory!.FullName, "third_party", "Real-ESRGAN-ncnn-vulkan", "src", "main.cpp"));

        Assert.Contains("std::to_wstring(scale)", source);
        Assert.Contains("std::to_string(scale)", source);
        Assert.Contains("modelname == PATHSTR(\"realesr-animevideov3\")", source);
        Assert.Contains("const path_t model_prefix = model + PATHSTR(\"/\") + modelname + scale_suffix;", source);
        Assert.Contains("sanitize_filepath(model_prefix + PATHSTR(\".param\"))", source);
        Assert.Contains("sanitize_filepath(model_prefix + PATHSTR(\".bin\"))", source);
        Assert.DoesNotContain("swprintf(parampath", source);
        Assert.DoesNotContain("sprintf(parampath", source);
        Assert.DoesNotContain("wchar_t parampath[256]", source);
        Assert.DoesNotContain("char parampath[256]", source);
    }
}
