# Caustica build-46 / SPBRScandi audit

Date: 2026-10-07

## Scope

This document records the evidence for the Minecraft RTX assets distributed by
`grg914/dlss-nr-manager`:

- `Caustica-RTX-Minecraft-26.2-build-46.jar`
- `SPBRScandi.zip`

The goal is to distinguish verified provenance from assumptions based on filenames.

## Caustica source provenance

`Caustica-RTX/SOURCE.json` pins the vendored source to:

- repository: `grg914/Caustica-RTX`
- commit: `27575c2ecf8578ca908a49c5d484ff3cc1ed0c7a`

GitHub reports `grg914/Caustica-RTX` as a fork of
`AriesAlex/Caustica-RTX`, whose current main commit is
`e3e3ec10d0a1dd383d51f98a0cf974a469fc67f1`.

Comparing that parent commit to the pinned fork commit shows the fork is 6 commits
ahead and 0 behind. The delta includes, among other files:

- `native/ngx_shim/ngx_shim.cpp`
- `src/main/java/dev/comfyfluffy/caustica/CausticaConfig.java`
- `src/main/java/dev/comfyfluffy/caustica/client/RtVideoOptions.java`
- `src/main/java/dev/comfyfluffy/caustica/ngx/NgxLibrary.java`
- `src/main/java/dev/comfyfluffy/caustica/ngx/NgxRuntime.java`
- `src/main/java/dev/comfyfluffy/caustica/rt/RtComposite.java`
- `src/main/java/dev/comfyfluffy/caustica/rt/pipeline/RtDisplayPipeline.java`
- added `src/main/java/dev/comfyfluffy/caustica/rt/pipeline/RtDlssNr.java`
- display/world shader changes
- English/French option strings

The pinned source therefore is not the unmodified parent Caustica baseline.

## build-46 release provenance

The Caustica fork prerelease:

`scandicraft-26.2-build-46`

targets exactly:

`27575c2ecf8578ca908a49c5d484ff3cc1ed0c7a`

Caustica CI run #46 also ran on that exact commit and completed successfully.
The workflow jobs included:

- Windows NGX shim build
- Linux NGX shim build
- bundled mod JAR build
- Minecraft 26.2 prerelease publication

The publication job explicitly created the build-46 prerelease from
`${GITHUB_SHA}`.

The manager's currently distributed JAR has:

- size: 24,768,006 bytes
- SHA-256: `40cfc86cda627102a4bee304996233b231b172e9f17a5a8dc3dcbd85121b47c7`

The current build-46 asset on `grg914/Caustica-RTX` has the exact same size and
SHA-256. The manager therefore redistributes the exact current build-46 release
asset, not a different file that merely shares its name.

### Important release-history detail

The original Actions artifact produced by CI run #46 was independently retrieved
and inspected during this audit. Its bundled JAR is 43,990,797 bytes with SHA-256:

`a86a11e53f8c903501b4b5f66adf8124e78e7e80decb952ebcdb50d6ad6249e7`

The current build-46 release JAR was uploaded by the `grg914` account later
(2026-10-06 18:22:56Z), after the initial CI publication. It is therefore not
byte-identical to the original Actions artifact. This fact must remain visible:
the audit must validate the exact current release JAR rather than treating the
original CI artifact as equivalent.

## Verified custom implementation in source

The pinned source contains explicit implementation/configuration for:

- DLSS Ray Reconstruction
- DLSS Frame Generation
- NVIDIA Reflex
- capability-gated DLSS Neural Rendering / 3D-Guided Neural Rendering
- RTX Performance Mode
- native ScandiShader RTX look

The custom DLSS-NR pipeline is represented by `RtDlssNr.java` and native NGX
bindings in `NgxLibrary` / `ngx_shim`.

## Current release JAR structural evidence

The manager PR asset audit checks the exact currently distributed manager-owned
JAR, after validating its GitHub SHA-256 digest.

The first CI attempt (DLSS NR Manager Build #670) reached the custom-marker stage.
That means the JAR had already passed:

- `fabric.mod.json` presence/parsing
- mod id `caustica`
- name `Caustica RTX`
- RTX fork version
- Minecraft 26.2 dependency
- required custom classes:
  - `CausticaConfig.class`
  - `NgxLibrary.class`
  - `RtLookPackage.class`
  - `RtDlssRr.class`
  - `RtDlssFg.class`
  - `RtDlssNr.class`

Build #670 then failed because the verifier searched configuration literals in
the outer `CausticaConfig.class`. Java compiles these settings into nested
classes such as:

- `CausticaConfig$Rt$DlssNr.class`
- `CausticaConfig$Rt$PostFx.class`
- `CausticaConfig$Rt$Performance.class`
- `CausticaConfig$Rt$Fg.class`

The verifier has been corrected to inspect those nested classes.

## SPBRScandi integration

`SPBRScandi.zip` is a Minecraft resource pack, not a shaderpack and not an
embedded Caustica JAR resource.

The manager installs it to:

`<minecraft-instance>/resourcepacks/SPBRScandi.zip`

Minecraft then loads it through the normal resource-pack mechanism. A literal
reference to `SPBRScandi.zip` inside the Caustica JAR is therefore neither
required nor expected.

The manager describes the pack as a validated SPBR-based hybrid that keeps the
SPBR LabPBR terrain/material base and adds validated Scandi sky, End, GUI and
visual assets.

The separate optional `ScandiShaderV2.zip` is staged under `shaderpacks` only
for non-Caustica use. Caustica RTX uses its native ScandiShader RTX look instead
of executing an Iris/OptiFine shaderpack.

## Automated release guard added by PR #60

`tools/verify-caustica-scandi-assets.ps1` now verifies:

### Caustica JAR

- safe archive paths
- Fabric metadata
- Caustica RTX identity/version
- Minecraft 26.2 target
- custom RTX class presence
- DLSS-NR config/native ABI/pipeline markers
- Frame Generation config marker
- RTX Performance Mode marker
- native ScandiShader RTX marker

### SPBRScandi

- safe archive paths
- one top-level `pack.mcmeta`
- valid pack metadata
- Minecraft texture assets
- LabPBR `*_n.png` normal maps
- LabPBR `*_s.png` specular maps
- pinned SHA-256 during release

Both Build and Release workflows use the verifier.

## Status

IN PROGRESS.

The first audit run found and exposed a verifier-path bug, which has been
corrected. Corrected DLSS NR Manager Build #671 is the required acceptance test.

Do not mark the exact distributed build-46 / SPBRScandi assets VALIDATED until
that corrected workflow passes.
