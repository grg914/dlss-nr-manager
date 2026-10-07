function New-DeterministicFabricProfile {
    param(
        [Parameter(Mandatory=$true)]$LoaderInfo,
        [Parameter(Mandatory=$true)][string]$MinecraftVersion,
        [Parameter(Mandatory=$true)][string]$LoaderVersion
    )

    $launcherMeta = $LoaderInfo.launcherMeta
    if (-not $launcherMeta) {
        throw "Fabric loader metadata exposes no launcherMeta for $MinecraftVersion / $LoaderVersion."
    }

    $libraries = @()

    foreach ($library in @($launcherMeta.libraries.common)) {
        $name = [string]$library.name
        $url = [string]$library.url
        if ([string]::IsNullOrWhiteSpace($name) -or [string]::IsNullOrWhiteSpace($url)) {
            throw "Fabric launcher metadata contains an invalid common library."
        }

        $libraries += [ordered]@{
            name = $name
            url = $url
        }
    }

    # Minecraft 26.x is unobfuscated, so Fabric no longer adds intermediary
    # as a launcher library. Older targets still require it.
    if ($MinecraftVersion -notmatch "^26(?:\.|$)") {
        $intermediaryMaven = [string]$LoaderInfo.intermediary.maven
        if ([string]::IsNullOrWhiteSpace($intermediaryMaven)) {
            throw "Fabric loader metadata exposes no intermediary coordinate for $MinecraftVersion."
        }

        $libraries += [ordered]@{
            name = $intermediaryMaven
            url = "https://maven.fabricmc.net/"
        }
    }

    $loaderMaven = [string]$LoaderInfo.loader.maven
    if ([string]::IsNullOrWhiteSpace($loaderMaven)) {
        throw "Fabric loader metadata exposes no loader Maven coordinate."
    }

    $libraries += [ordered]@{
        name = $loaderMaven
        url = "https://maven.fabricmc.net/"
    }

    foreach ($library in @($launcherMeta.libraries.client)) {
        $name = [string]$library.name
        $url = [string]$library.url
        if ([string]::IsNullOrWhiteSpace($name) -or [string]::IsNullOrWhiteSpace($url)) {
            throw "Fabric launcher metadata contains an invalid client library."
        }

        $libraries += [ordered]@{
            name = $name
            url = $url
        }
    }

    $mainClass = if ($launcherMeta.mainClass -is [string]) {
        [string]$launcherMeta.mainClass
    }
    elseif ($launcherMeta.mainClass.PSObject.Properties["client"]) {
        [string]$launcherMeta.mainClass.client
    }
    else {
        ""
    }

    if ([string]::IsNullOrWhiteSpace($mainClass)) {
        throw "Fabric launcher metadata exposes no client main class."
    }

    $profileId = "fabric-loader-$LoaderVersion-$MinecraftVersion"
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
        [Parameter(Mandatory=$true)]$LoaderInfo,
        [Parameter(Mandatory=$true)][string]$MinecraftVersion,
        [Parameter(Mandatory=$true)][string]$LoaderVersion,
        [Parameter(Mandatory=$true)][string]$OutputPath
    )

    $profile = New-DeterministicFabricProfile -LoaderInfo $LoaderInfo -MinecraftVersion $MinecraftVersion -LoaderVersion $LoaderVersion
    $json = $profile | ConvertTo-Json -Depth 16 -Compress
    [IO.File]::WriteAllText($OutputPath, $json, [Text.UTF8Encoding]::new($false))
    return $profile
}
