param(
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [Parameter(Mandatory = $true)][string]$PublisherPath
)
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSEdition -ne "Desktop" -or $PSVersionTable.PSVersion.Major -ne 5) {
    throw "This smoke test must run in a fresh Windows PowerShell 5.1 process."
}
$global:FakeAssets = @()
$global:Uploads = 0
function global:gh {
    $global:LASTEXITCODE = 0
    if ($args[0] -eq "api") {
        return (ConvertTo-Json -InputObject @{ draft = $false; assets = @($global:FakeAssets) } -Depth 7 -Compress)
    }
    if ($args[0] -eq "release" -and $args[1] -eq "upload") {
        $path = [string]$args[3]
        $name = [IO.Path]::GetFileName($path)
        if (@($global:FakeAssets | Where-Object { $_.name -eq $name }).Count) {
            throw "Mock forbids replacement of existing asset."
        }
        $global:FakeAssets += [pscustomobject]@{
            name = $name
            digest = "sha256:" + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            size = (Get-Item -LiteralPath $path).Length
        }
        $global:Uploads++
        return
    }
    throw "Unexpected GitHub mock call: $($args -join ' ')"
}
try {
    & $PublisherPath -Path $ZipPath
    if ($global:Uploads -ne 1) { throw "Initial publish did not occur exactly once." }
    & $PublisherPath -Path $ZipPath
    if ($global:Uploads -ne 1) { throw "An unchanged ZIP was published twice." }
    Write-Host "PASS: Windows PowerShell 5.1 ZIP assembly loaded, mock publication and idempotence validated."
}
finally {
    Remove-Item Function:\gh -ErrorAction SilentlyContinue
}
