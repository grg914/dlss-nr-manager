# DLSS NR Manager

A native Windows manager for installing and maintaining the experimental **OptiScaler DLSS Neural Rendering (DLSSNR)** fork.

> Supports automatic candidate detection across **Steam, Epic and GOG**, with upstream-validated profiles for Cyberpunk 2077, Baldur's Gate 3 and Hogwarts Legacy, plus NVIDIA RTX 20/30/40/50 GPUs.

## Screenshots

> Screenshot placeholder — main Cyberpunk card, installation status, GPU/runtime validation, actions and integrated logs.

## Features

- WPF / .NET 8, Windows x64
- Self-contained single-file executable
- Installed-game scanning through Steam, Epic and GOG manifests/registry
- Compatibility confidence: Validated / Probable / Candidate
- Upstream-validated target paths for Cyberpunk 2077, Baldur's Gate 3 and Hogwarts Legacy
- Heuristic detection using DLSS / Streamline / XeSS / FidelityFX runtime signals
- Manual game-folder selection fallback
- NVIDIA GPU and RTX-generation detection
- Stable/prerelease selection from the upstream OptiScaler-DLSSNR fork
- Downloads the complete upstream release archive
- Never downloads or redistributes the proprietary `nvngx_dlssnr.dll` runtime
- Requires the user to select their own runtime and verifies SHA-256
- RTX 50 expected SHA-256: `E16BCF15E16E13F527491CDF7845B2FE6521A738D8F7C9C721866A8496E1FC8E`
- RTX 20/30/40 compatibility-runtime SHA-256: `E67DEE209320CDAFE0E93E45675D7AA34323A53ACC57A72B2E40A181581C989A`
- Full managed-install backup before changes
- Install, update, restore and uninstall
- Detects existing proxy DLLs and refuses to overwrite unknown loaders silently
- Cyberpunk proxy recommendation: `dbghelp.dll`; `dxgi.dll` fallback
- Integrated `OptiScaler.log` viewer
- Neural Rendering preset only edits keys that already exist in the current upstream INI
- OptiScaler overlay is forced on with F10 (`ShortcutKey=0x79`)

## Default Neural Rendering preset

For the current upstream INI the manager sets the following conservative Neural Rendering defaults under `[DlssNr]`:

```ini
Enabled=true
RunBeforeSR=true
Passes=1
WorkingScale=1.0
Style=1
```

The upstream configuration documents `Style=1` as **Natural**. The manager does not create unsupported keys; it only updates keys present in the extracted `OptiScaler.ini`.

## Installation

1. Download the Windows x64 artifact/release.
2. Close Cyberpunk 2077 and its launcher.
3. Run `DlssNrManager.exe`.
4. Confirm the detected game folder and GPU.
5. Select your separately obtained `nvngx_dlssnr.dll`.
6. Ensure the runtime hash is valid for the detected RTX generation.
7. Leave `dbghelp.dll` selected unless your loader setup requires another compatible proxy.
8. Click **Install**.

The manager installs into `Cyberpunk 2077\bin\x64`.

## Loader compatibility

Cyberpunk installations using **Cyber Engine Tweaks (CET), RED4ext, ReShade or other DLL loaders** can already own common proxy names. DLSS NR Manager will not silently overwrite a proxy DLL it cannot identify as OptiScaler.

Upstream specifically validates `dbghelp.dll` for Cyberpunk. `dxgi.dll` is the recommended fallback. Avoid `d3d12.dll` when Cyberpunk Ray Reconstruction becomes unavailable/greyed out because upstream documents a Streamline conflict resolved by switching to `dxgi.dll`.

Correct chaining of third-party loaders is configuration-specific and is intentionally not automated in V1.

## Runtime licensing

`nvngx_dlssnr.dll` is not distributed by this project. You must supply it yourself. DLSS NR Manager only verifies the selected file and copies it locally into the game directory.

## Upstream

This project automates installation of:
- `wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass`

OptiScaler and NVIDIA components retain their respective licenses and ownership. DLSS NR Manager itself is MIT licensed.

## Limitations

- Automatic detection is evidence-based. `Validated` means the target path is explicitly documented upstream; `Probable` and `Candidate` still require in-game verification.
- GPU detection relies on Windows display-adapter registry data.
- No automatic third-party loader chaining.
- No proprietary runtime download.
- Update availability is based on the selected upstream release channel.
- The manager should not be used to inject mods into anti-cheat protected multiplayer games.

## Build

```powershell
dotnet restore
dotnet publish DlssNrManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

GitHub Actions publishes `DlssNrManager.exe` as a build artifact. Tags matching `v*` also create a GitHub Release with the executable and ZIP.
