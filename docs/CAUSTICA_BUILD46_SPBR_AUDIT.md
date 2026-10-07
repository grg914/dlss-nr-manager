# Caustica build-46 / SPBRScandi audit

Date: 2026-10-07
Status: VALIDATED by DLSS NR Manager Build #673

## Scope

This audit covers the Minecraft RTX assets distributed by
`grg914/dlss-nr-manager`:

- `Caustica-RTX-Minecraft-26.2-build-46.jar`
- `SPBRScandi.zip`

The objective is to distinguish verified provenance and binary structure from
assumptions based on filenames.

## Caustica source provenance

`Caustica-RTX/SOURCE.json` pins the vendored source to:

- repository: `grg914/Caustica-RTX`
- commit: `27575c2ecf8578ca908a49c5d484ff3cc1ed0c7a`

GitHub identifies that repository as a fork of `AriesAlex/Caustica-RTX`.

The official parent release `v0.1.0-rtx.2` resolves to commit:

`e3e3ec10d0a1dd383d51f98a0cf974a469fc67f1`

Comparing that parent commit to the pinned fork commit shows the fork is 6
commits ahead and 0 behind. The delta includes, among other files:

- `native/ngx_shim/ngx_shim.cpp`
- `src/main/java/dev/comfyfluffy/caustica/CausticaConfig.java`
- `src/main/java/dev/comfyfluffy/caustica/client/RtVideoOptions.java`
- `src/main/java/dev/comfyfluffy/caustica/ngx/NgxLibrary.java`
- `src/main/java/dev/comfyfluffy/caustica/ngx/NgxRuntime.java`
- `src/main/java/dev/comfyfluffy/caustica/rt/RtComposite.java`
- `src/main/java/dev/comfyfluffy/caustica/rt/pipeline/RtDisplayPipeline.java`
- added `src/main/java/dev/comfyfluffy/caustica/rt/pipeline/RtDlssNr.java`
- display/world shader changes
- English/French RTX option strings

The pinned source is therefore not the unmodified parent Caustica baseline.

## Binary comparison

Official parent release `AriesAlex/Caustica-RTX v0.1.0-rtx.2`:

- size: 24,747,827 bytes
- SHA-256: `07d1157513f390d222b7c6082cb74343adabff0a1774b33723c6325cc859c6e4`

Current project build-46 release asset:

- size: 24,768,006 bytes
- SHA-256: `40cfc86cda627102a4bee304996233b231b172e9f17a5a8dc3dcbd85121b47c7`

The current manager v3.1.1 publishes the exact same build-46 digest
`40cfc86c...`. It is therefore byte-distinct from the official parent
v0.1.0-rtx.2 binary.

DLSS NR Manager Build #673 additionally inspected the exact currently
distributed manager-owned JAR and passed the semantic/structural checks below.

## build-46 release provenance

The `grg914/Caustica-RTX` prerelease:

`scandicraft-26.2-build-46`

targets exactly:

`27575c2ecf8578ca908a49c5d484ff3cc1ed0c7a`

Caustica CI run #46 also ran on that exact commit and completed successfully.
Its workflow jobs included:

- Windows NGX shim build
- Linux NGX shim build
- bundled mod JAR build
- Minecraft 26.2 prerelease publication

The publication job created the build-46 prerelease against `${GITHUB_SHA}`.

### Release-history detail

The original Actions artifact produced by Caustica CI run #46 was independently
retrieved during this audit. Its bundled JAR is 43,990,797 bytes with SHA-256:

`a86a11e53f8c903501b4b5f66adf8124e78e7e80decb952ebcdb50d6ad6249e7`

The current build-46 release JAR was later uploaded by the `grg914` account on
2026-10-06 18:22:56Z. It is not byte-identical to that initial Actions artifact.

This is intentionally recorded instead of hidden: all manager validation now
targets the exact current release JAR that users receive.

## Verified current JAR structure

DLSS NR Manager Build #673 reports:

- Caustica JAR structural/custom-marker audit: PASSED
- version: `0.1.0-rtx.2`
- Minecraft: `26.2`

The verifier requires these custom classes:

- `CausticaConfig.class`
- `NgxLibrary.class`
- `RtLookPackage.class`
- `RtDlssRr.class`
- `RtDlssFg.class`
- `RtDlssNr.class`

It also verifies custom markers in the compiled nested config/native/pipeline
classes for:

- DLSS Neural Rendering configuration
- DLSS-NR native ABI
- DLSS-NR Java pipeline
- DLSS Frame Generation
- RTX Performance Mode
- native ScandiShader RTX look

The first audit attempt exposed a verifier bug: Java nested settings are compiled
to `CausticaConfig$Rt$*.class`, not the outer `CausticaConfig.class`. The
corrected verifier checks the nested classes and Build #673 passes.

## SPBRScandi structure and integration

`SPBRScandi.zip` is a Minecraft resource pack, not a shaderpack and not an
embedded Caustica JAR resource.

The manager installs it to:

`<minecraft-instance>/resourcepacks/SPBRScandi.zip`

Minecraft loads it through its normal resource-pack mechanism. A literal
reference to `SPBRScandi.zip` inside the Caustica JAR is therefore neither
required nor expected.

The manager describes the pack as a validated SPBR-based hybrid that keeps the
SPBR LabPBR terrain/material base and adds validated Scandi sky, End, GUI and
visual assets.

Build #673 inspected the exact manager-owned `SPBRScandi.zip` and reports:

- resource-pack audit: PASSED
- texture PNG files: 10,905
- LabPBR normal maps: 2,395
- LabPBR specular maps: 3,720

The separate optional `ScandiShaderV2.zip` is staged under `shaderpacks` only
for non-Caustica use. Caustica RTX uses its native ScandiShader RTX look instead
of executing an Iris/OptiFine shaderpack.

## Automated release guard

`tools/verify-caustica-scandi-assets.ps1` validates:

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

## Acceptance evidence

DLSS NR Manager GitHub Actions:

- Build #670: FAILED because the first verifier incorrectly inspected the outer
  Java config class.
- Build #672: FAILED because an intermediate correction used unescaped `$Rt`
  inside PowerShell double-quoted paths.
- Build #673: PASSED, including asset audit, zero-upstream audit, offline restore,
  regression tests, Windows publish and executable/icon verification.

Conclusion: the exact Caustica build-46 and SPBRScandi assets currently
distributed by DLSS NR Manager are structurally validated, provenance-tracked,
and protected by CI/release guards.
