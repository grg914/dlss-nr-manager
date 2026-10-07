function New-DlssNrFabric262ClientProfile {
    param(
        [Parameter(Mandatory=$true)]$LoaderInfo,
        [Parameter(Mandatory=$true)][string]$MinecraftVersion
    )

    if ($MinecraftVersion -ne "26.2") {
        throw "Deterministic Fabric profile generation currently supports Minecraft 26.2 only. Requested: $MinecraftVersion"
    }

    $loaderVersion = [string]$LoaderInfo.loader.version
    $loaderMaven = [string]$LoaderInfo.loader.maven
    $launcherMeta = $LoaderInfo.launcherMeta

    if ([string]::IsNullOrWhiteSpace($loaderVersion) -or
        [string]::IsNullOrWhiteSpace($loaderMaven) -or
        -not $launcherMeta) {
        throw "Fabric loader metadata is incomplete for Minecraft $MinecraftVersion."
    }

    $mainClassElement = $launcherMeta.mainClass
    $mainClass = if ($mainClassElement -is [string]) {
        [string]$mainClassElement
    }
    elseif ($mainClassElement -and $mainClassElement.client) {
        [string]$mainClassElement.client
    }
    else {
        ""
    }

    if ([string]::IsNullOrWhiteSpace($mainClass)) {
        throw "Fabric launcher metadata exposes no client main class."
    }

    $libraries = New-Object System.Collections.ArrayList

    foreach ($library in @($launcherMeta.libraries.common)) {
        $name = [string]$library.name
        $url = [string]$library.url
        if ([string]::IsNullOrWhiteSpace($name) -or [string]::IsNullOrWhiteSpace($url)) {
            throw "Fabric launcher metadata contains an invalid common library entry."
        }

        [void]$libraries.Add([ordered]@{
            name = $name
            url = $url
        })
    }

    # Minecraft 26.2 is distributed unobfuscated. Fabric's official
    # ProfileHandler therefore does not add intermediary to the launcher
    # profile for this game version; it adds only fabric-loader here.
    [void]$libraries.Add([ordered]@{
        name = $loaderMaven
        url = "https://maven.fabricmc.net/"
    })

    foreach ($library in @($launcherMeta.libraries.client)) {
        $name = [string]$library.name
        $url = [string]$library.url
        if ([string]::IsNullOrWhiteSpace($name) -or [string]::IsNullOrWhiteSpace($url)) {
            throw "Fabric launcher metadata contains an invalid client library entry."
        }

        [void]$libraries.Add([ordered]@{
            name = $name
            url = $url
        })
    }

    $profileId = "fabric-loader-$loaderVersion-$MinecraftVersion"
    $deterministicTime = "1970-01-01T00:00:00+0000"

    return [ordered]@{
        id = $profileId
        inheritsFrom = $MinecraftVersion
        releaseTime = $deterministicTime
        time = $deterministicTime
        type = "release"
        mainClass = $mainClass
        arguments = [ordered]@{
            game = @()
            jvm = @("-DFabricMcEmu= net.minecraft.client.main.Main ")
        }
        libraries = @($libraries)
    }
}

function ConvertTo-DlssNrCanonicalFabricProfileJson {
    param(
        [Parameter(Mandatory=$true)]$Profile
    )

    return (($Profile | ConvertTo-Json -Depth 16 -Compress) + [Environment]::NewLine)
}

function Write-DlssNrFabricProfile {
    param(
        [Parameter(Mandatory=$true)]$Profile,
        [Parameter(Mandatory=$true)][string]$Path
    )

    $json = ConvertTo-DlssNrCanonicalFabricProfileJson -Profile $Profile
    [IO.File]::WriteAllText(
        $Path,
        $json,
        [Text.UTF8Encoding]::new($false))
}
