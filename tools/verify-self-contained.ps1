param([switch]$Strict)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

$required = @(
  "Caustica-RTX",
  "third_party/NVIDIA-Streamline",
  "third_party/OptiScaler",
  "third_party/video2dlssnr",
  "third_party/FFmpeg",
  "third_party/Real-ESRGAN-ncnn-vulkan",
  "third_party/ReShade",
  "third_party/onnxruntime",
  "third_party/ai-models/ai-image-detection-ONNX",
  "third_party/ai-models/ai-image-detect-distilled-ONNX"
)

$missing = @()
foreach ($relative in $required) {
  $path = Join-Path $Root $relative
  if (!(Test-Path $path)) { $missing += $relative }
}

$patterns = @(
  "grg914/Caustica-RTX",
  "NVIDIA-RTX/Streamline",
  "wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass",
  "DaniilSokolyuk/video2dlssnr",
  "BtbN/FFmpeg-Builds",
  "huggingface.co/onnx-community",
  "raw.githubusercontent.com/Tohrusky",
  "raw.githubusercontent.com/itsspin",
  "ScoopInstaller/Versions",
  "api.modrinth.com",
  "meta.fabricmc.net",
  "maven.fabricmc.net"
)

$scanRoots = @("Services", ".github/workflows")
$references = @()
foreach ($scanRoot in $scanRoots) {
  $base = Join-Path $Root $scanRoot
  if (!(Test-Path $base)) { continue }
  Get-ChildItem $base -Recurse -File | Where-Object { $_.Extension -in ".cs", ".yml", ".yaml", ".ps1" } | ForEach-Object {
    $file = $_
    $lineNumber = 0
    Get-Content $file.FullName | ForEach-Object {
      $lineNumber++
      $line = $_
      foreach ($pattern in $patterns) {
        if ($line -like "*$pattern*") {
          $references += [pscustomobject]@{
            File = [IO.Path]::GetRelativePath($Root, $file.FullName)
            Line = $lineNumber
            Pattern = $pattern
            Text = $line.Trim()
          }
        }
      }
    }
  }
}

Write-Host "Self-contained dependency audit"
Write-Host "==============================="
Write-Host ""
if ($missing.Count -eq 0) {
  Write-Host "Required mirrored source folders: OK"
} else {
  Write-Host "Missing mirrored source folders:"
  $missing | ForEach-Object { Write-Host "  - $_" }
}

Write-Host ""
if ($references.Count -eq 0) {
  Write-Host "Direct upstream dependency references in runtime/release code: NONE"
} else {
  Write-Host "Direct upstream dependency references still present:"
  $references | Sort-Object File, Line | Format-Table File, Line, Pattern -AutoSize
}

Write-Host ""
if (Test-Path (Join-Path $Root "third_party-local/NVIDIA-DLSS")) {
  Write-Host "Local NVIDIA DLSS SDK: present (local-only / Git-ignored)"
} else {
  Write-Host "Local NVIDIA DLSS SDK: absent (required only for local Caustica native rebuilds)"
}

$failed = $missing.Count -gt 0 -or $references.Count -gt 0
if ($Strict -and $failed) { exit 1 }
exit 0