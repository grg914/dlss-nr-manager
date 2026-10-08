# VLC Video Enhancement

The **VSR-HDR Vidéo** menu exposes two internal submenus:

1. **VSR-HDR VLC (Direct)**: real-time playback through VLC Direct3D11;
2. **Restore HD Vidéo**: permanent offline restoration/upscale saved as a new video file.

VLC remains an external portable runtime rather than being linked into the MIT-licensed DLSS NR Manager executable.

## VLC source and runtime provenance

Target:

- VLC 3.0.24 on Windows x64;
- source mirror: `https://github.com/grg914/vlc`;
- source commit: `6de05adcbaf2e8b85fe86aad4169393098628119`;
- official runtime asset: `vlc-3.0.24-win64.zip`;
- official runtime checksum: read and verified from VideoLAN's matching `.sha256` file during the refresh;
- the resulting runtime SHA-256 is persisted in `vlc-3.0.24-provenance.json` and re-verified after upload to the manager-owned release.

`tools/publish-vlc-runtime.ps1` verifies that the pinned source commit exists in `grg914/vlc`, verifies the official VideoLAN runtime and source archives, and mirrors the runtime, corresponding source archive and provenance metadata into the manager-owned runtime seed.

## Real-time VLC mode

DLSS NR Manager launches VLC with its Direct3D11 output module and per-launch options:

- `--d3d11-upscale-mode=super` enables the NVIDIA RTX VSR path;
- the user-facing artifact-reduction toggle is separate, but VLC/NVIDIA expose artifact cleanup through that same RTX VSR D3D11 extension, so requesting artifact reduction also requests the VSR path;
- `--d3d11-hdr-mode=generate` requests SDR to HDR generation / NVIDIA TrueHDR;
- `--d3d11-hdr-mode=auto|always|never` selects the HDR output policy;
- windowed x1/x2/x4 uses VLC's inherited `--zoom` option to request a larger presentation surface;
- fullscreen uses `--fullscreen --autoscale` and targets the active display resolution directly.

The original local video is never modified or transcoded.

### In-video VSR/HDR indicator

When enabled in the Direct submenu, DLSS NR Manager adds VLC's native `marq` OSD source:

- `● VSR` when the NVIDIA VSR path is active;
- `● HDR` when SDR→HDR / forced HDR enhancement is active;
- `● VSR • HDR` when both are active;
- position `6` = top-right (`RIGHT | TOP`);
- small semi-transparent text (`18 px`, opacity `220/255`);
- no OSD is injected when the indicator toggle is disabled or no direct enhancement is active.

The badge is rendered by VLC itself and therefore remains visible in fullscreen without injecting a graphics DLL into VLC.

### x1 / x2 / x4 semantics

The scale selector is a presentation target for windowed playback, not a claim that NVIDIA exposes a fixed “2-pass” or “4-pass” VSR mode.

- x1: native-size VLC window;
- x2: 2x presentation window;
- x4: 4x presentation window;
- fullscreen: monitor resolution; the x1/x2/x4 selector is disabled because additional zoom would crop rather than increase the physical output resolution.

VSR only activates when the driver, GPU, source format and presentation size make the NVIDIA Direct3D11 path available.

## Restore HD Vidéo

Permanent mode creates a new video in the selected output folder. Neural Rendering, conservative artifact cleanup and AI upscale are independent user-facing stages.

- x1: Neural Rendering at native resolution using the manager-owned `video2dlssnr` pipeline;
- x2: optional Neural Rendering pre-pass, then Real-ESRGAN x2;
- x4: optional Neural Rendering pre-pass, then Real-ESRGAN x4;
- processing and encoding are local;
- the source video is never overwritten;
- the operation can be cancelled.

This workflow deliberately does **not** claim to “bake NVIDIA VSR / RTX Video HDR into the file”. Those are display-time D3D11 effects. Permanent HDR generation should only be added when a separately validated inverse-tone-mapping/metadata pipeline exists.

## Distribution model

Production boundaries:

- `DlssNrManager.exe`: MIT;
- VLC executable/runtime: GPL-2.0-or-later, external process;
- libVLC portions: LGPL-2.1-or-later;
- matching VLC source archive is distributed alongside the binary runtime;
- the pinned source mirror remains available at `grg914/vlc`.

## DRM boundary

The feature is intended for local media files VLC can normally decode and present. DLSS NR Manager does not bypass protected-media paths, DRM, HDCP, encrypted streaming services or protected D3D surfaces.

## HDR requirements

Real-time SDR to HDR generation requires a compatible NVIDIA GPU/driver, an HDR-capable display and Windows HDR output. If the GPU/driver/display path cannot create the requested D3D11 tone-mapping path, DLSS NR Manager does not attempt to intercept or bypass unsupported surfaces.


## Offline bundle

Production releases also create `DlssNrManager-vlc-offline-win-x64.zip`.

It contains:

- `DlssNrManager.exe`;
- `vlc-3.0.24-win64.zip`;
- `vlc-3.0.24-provenance.json`;
- the matching `vlc-3.0.24.tar.xz` source archive.

When those files are next to the application executable, VLC setup verifies the bundled runtime against the provenance file and installs it into `%LOCALAPPDATA%\DlssNrManager\vlc` without any network access. Once installed, playback continues to use that local runtime offline.


## Évaluation vidéo floue

La stratégie de restauration pour vidéos floues, les limites x2/x4, les risques de détails inventés et l'évaluation de moteurs temporels sont documentés dans [VIDEO_BLUR_RESTORATION_EVALUATION.md](VIDEO_BLUR_RESTORATION_EVALUATION.md).
