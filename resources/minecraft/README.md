# Minecraft RTX / DLSS resources

This directory describes the Minecraft Java 26.2 profile used by DLSS NR Manager.

## Open-source components downloaded at runtime

The manager downloads compatible release assets directly from their upstream GitHub projects:

- Fabric Loader / installer — `FabricMC/fabric-installer`
- Fabric API — `FabricMC/fabric-api`
- Caustica RTX — `AriesAlex/Caustica-RTX`

Reference-only projects are listed in `minecraft-profile.json`; they are not installed automatically unless a future profile explicitly supports their Minecraft version.

## Proprietary DLSS / Streamline runtime files

NVIDIA DLSS/NGX and Streamline binaries are **not redistributed by this repository**.

The manager can inspect a DLSS package supplied locally by the user and stage only the files selected by the Minecraft profile into:

`<minecraft instance>/.dlss-nr-manager-runtime/`

`dlss5-package-manifest.json` contains filenames, sizes and SHA-256 fingerprints for the project owner's validated local package, including:

- `nvngx_dlss.dll`
- `nvngx_dlssg.dll`
- `nvngx_dlssnr.dll`
- `sl.common.dll`
- `sl.interposer.dll`
- `sl.dlss.dll`
- `sl.dlss_g.dll`
- `sl.dlss_nr.dll`
- `sl.reflex.dll`

The specific `nvngx_dlssnr.dll` fingerprint currently validated for RTX 50 is:

`E16BCF15E16E13F527491CDF7845B2FE6521A738D8F7C9C721866A8496E1FC8E`

The manager does not claim that simply copying these files activates a feature in Minecraft. Activation still depends on the selected renderer/provider supporting the corresponding NVIDIA API. Caustica RTX is installed from its upstream release and controls its own renderer integration.

## Safety / compatibility

- Back up managed files before changing the Minecraft instance.
- Do not automatically combine multiple world-renderer replacements.
- Minecraft 26.2 profile expects Java 25 and Fabric.
- The current uploaded DLSS package does not contain `sl.dlss_d.dll` or `nvngx_dlssd.dll`; those are therefore not staged as a Ray Reconstruction Streamline pair.
