param(
    [Parameter(Mandatory = $true)]
    [string]$ZipPath
)

$ErrorActionPreference = "Stop"

$ExpectedSlDlssNrSha256 = "9f6672e5e0170dc118a3188d21bda187e1fc1aa3502895b21ab846d23165c11d"
$ExpectedNvngxDlssNrSha256 = "e16bcf15e16e13f527491cdf7845b2fe6521a738d8f7c9c721866a8496e1fc8e"

if (!(Test-Path -LiteralPath $ZipPath -PathType Leaf)) {
    throw "Streamline runtime ZIP was not found: $ZipPath"
}

$required = @(
    "sl.interposer.dll",
    "sl.common.dll",
    "sl.dlss.dll",
    "nvngx_dlss.dll",
    "sl.dlss_g.dll",
    "nvngx_dlssg.dll",
    "sl.reflex.dll",
    "NvLowLatencyVk.dll",
    "sl.dlss_nr.dll",
    "nvngx_dlssnr.dll"
)

$temp = Join-Path ([IO.Path]::GetTempPath()) ("DlssNrManager-StreamlineAudit-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $temp -Force

    $resolved = @{}

    foreach ($name in $required) {
        $matches = @(Get-ChildItem -LiteralPath $temp -Filter $name -File -Recurse -ErrorAction SilentlyContinue)
        if ($matches.Count -ne 1) {
            throw "Expected exactly one $name in the manager-owned Streamline bundle, found $($matches.Count)."
        }

        $resolved[$name] = $matches[0].FullName
    }

    $slHash = (Get-FileHash -LiteralPath $resolved["sl.dlss_nr.dll"] -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($slHash -ne $ExpectedSlDlssNrSha256) {
        throw "sl.dlss_nr.dll SHA-256 mismatch. Expected=$ExpectedSlDlssNrSha256 Actual=$slHash"
    }

    $ngxHash = (Get-FileHash -LiteralPath $resolved["nvngx_dlssnr.dll"] -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($ngxHash -ne $ExpectedNvngxDlssNrSha256) {
        throw "nvngx_dlssnr.dll SHA-256 mismatch. Expected=$ExpectedNvngxDlssNrSha256 Actual=$ngxHash"
    }

    $sig = Get-AuthenticodeSignature -LiteralPath $resolved["nvngx_dlssnr.dll"]
    $signer = if ($sig.SignerCertificate) { [string]$sig.SignerCertificate.Subject } else { "" }

    if ($sig.Status -ne "Valid" -or $signer -notmatch "(?i)NVIDIA") {
        throw "nvngx_dlssnr.dll is not a valid NVIDIA-signed runtime. Status=$($sig.Status) Signer=$signer"
    }

    Write-Host "Manager-owned Streamline runtime audit: PASSED"
    Write-Host "  Required runtime files: $($required.Count)"
    Write-Host "  sl.dlss_nr.dll SHA-256: $slHash"
    Write-Host "  nvngx_dlssnr.dll SHA-256: $ngxHash"
    Write-Host "  nvngx_dlssnr.dll signer: $signer"
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
