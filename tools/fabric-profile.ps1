function New-DeterministicFabricProfile {
    param(
        [Parameter(Mandatory=$true)]$FabricLoader,
        [Parameter(Mandatory=$true)][string]$MinecraftVersion
    )

    $loaderVersion = [string]$FabricLoader.version
    $profileId = [string]$FabricLoader.profile_id
    $mainClass = [string]$FabricLoader.main_class
    $expectedProfileId = "fabric-loader-$loaderVersion-$MinecraftVersion"

    if ([string]::IsNullOrWhiteSpace($loaderVersion)) {
        throw "Minecraft runtime lock exposes no Fabric Loader version."
    }
    if ([string]::IsNullOrWhiteSpace($profileId) -or $profileId -ne $expectedProfileId) {
        throw "Minecraft runtime lock has invalid Fabric profile id. Expected=$expectedProfileId Actual=$profileId"
    }
    if ([string]::IsNullOrWhiteSpace($mainClass)) {
        throw "Minecraft runtime lock exposes no Fabric client main class."
    }

    $libraries = @()
    foreach ($library in @($FabricLoader.libraries)) {
        $name = [string]$library.name
        $url = [string]$library.url

        if ([string]::IsNullOrWhiteSpace($name) -or [string]::IsNullOrWhiteSpace($url)) {
            throw "Minecraft runtime lock contains a Fabric library without name/url."
        }

        $uri = [Uri]$url
        if ($uri.Scheme -ne "https") {
            throw "Minecraft runtime lock contains a non-HTTPS Fabric library URL: $url"
        }

        $libraries += [ordered]@{
            name = $name
            url = $url
        }
    }

    if ($libraries.Count -eq 0) {
        throw "Minecraft runtime lock contains no Fabric libraries."
    }

    $stableTime = "1970-01-01T00:00:00+0000"
    return [ordered]@{
        id = $profileId
        inheritsFrom = $MinecraftVersion
        releaseTime = $stableTime
        time = $stableTime
        type = "release"
        mainClass = $mainClass
        arguments = [ordered]@{
            game = @()
            jvm = @("-DFabricMcEmu= net.minecraft.client.main.Main ")
        }
        libraries = @($libraries)
    }
}

function Write-DeterministicFabricProfile {
    param(
        [Parameter(Mandatory=$true)]$FabricLoader,
        [Parameter(Mandatory=$true)][string]$MinecraftVersion,
        [Parameter(Mandatory=$true)][string]$OutputPath
    )

    $profile = New-DeterministicFabricProfile -FabricLoader $FabricLoader -MinecraftVersion $MinecraftVersion
    $json = $profile | ConvertTo-Json -Depth 16 -Compress
    [IO.File]::WriteAllText($OutputPath, $json, [Text.UTF8Encoding]::new($false))
    return $profile
}
