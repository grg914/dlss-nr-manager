# NVIDIA runtime ownership

## Games & DLSS

The Games & DLSS page is the single generic NVIDIA DLSS / Streamline runtime manager.

Game runtime files come from the manager-owned `streamline-runtime-v*-win-x64.zip` asset published in DLSS NR Manager releases. A user may also select a local ZIP or a local `nvngx_dlssnr.dll`, which is validated before use.

Neural Rendering is complete only when the Streamline bundle contains both:

- `sl.dlss_nr.dll` — SHA-256 `9f6672e5e0170dc118a3188d21bda187e1fc1aa3502895b21ab846d23165c11d`
- `nvngx_dlssnr.dll` — SHA-256 `e16bcf15e16e13f527491cdf7845b2fe6521a738d8f7c9c721866a8496e1fc8e`

Existing game vendor DLLs are never overwritten during manager-owned staging.

## Minecraft RTX

Minecraft RTX exposes no generic DLSS / Streamline staging controls. Its path remains:

`Minecraft Java 26.2 -> Vulkan -> Caustica RTX -> native NVIDIA NGX / Streamline integration`

The existing Minecraft render policy still rejects generic OptiScaler/proxy artifacts in the manager-owned Minecraft runtime directory.

## Video

`video2dlssnr_release.zip` remains owned by the media/video pipeline. Games no longer resolve `nvngx_dlssnr.dll` from that package.

## Restricted bootstrap

The two Neural Rendering DLLs are not committed to Git. The Streamline publisher accepts controlled local inputs:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\publish-streamline-runtime.ps1 `
  -Repository grg914/dlss-nr-manager `
  -ReleaseTag <tag> `
  -NeuralStreamlineRuntimePath C:\path\sl.dlss_nr.dll `
  -NeuralNgxRuntimePath C:\path\nvngx_dlssnr.dll
```

The publisher validates their pinned SHA-256 values before packaging. Once a complete manager-owned bundle exists, automated refresh can preserve the validated pair from that release when local restricted inputs are unavailable.
