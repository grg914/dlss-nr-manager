# Local AI Studio

## Goal

Add a fully local image/video generation workspace to DLSS NR Manager without modifying the user's system Python/CUDA installation.

The UI is exposed as **AI Studio local** and contains four internal tabs:

- **Créer**: task, backend and model selection, prompt/source/mask/output, queue job.
- **Model Manager**: model catalogue, license/quality/hardware notes and install status.
- **Jobs**: persistent local generation queue.
- **Runtime**: isolated runtime workspace for Python, PyTorch, CUDA userspace, ComfyUI and Diffusers.

Runtime root:

`%LOCALAPPDATA%\DlssNrManager\ai-studio`

Subdirectories:

- `runtime/python`
- `runtime/comfyui`
- `runtime/diffusers`
- `runtime/wheelhouse`
- `models`
- `workflows`
- `jobs`
- `outputs`

The application never modifies global Python, global pip packages or a global CUDA Toolkit.

## Task selection

Supported task classes in the current catalogue:

- Text-to-Image
- Image-to-Image
- Inpainting / Outpainting
- Text-to-Video
- Image-to-Video
- Video-to-Video

The model selector is filtered automatically by task.

## Model policy

### Image

| Model | Tier | Tasks | License | Manager-owned redistribution |
| --- | --- | --- | --- | --- |
| FLUX.2 [klein] 4B | Recommended | T2I, I2I, editing/inpaint workflows | Apache-2.0 | Eligible |
| FLUX.2 [dev] | Maximum Quality | T2I, I2I, editing | FLUX [dev] Non-Commercial License v2.0 | Manual acceptance / no automatic redistribution |
| Qwen-Image-2.1 | Maximum Quality | T2I, I2I, editing | Qwen Research License | No automatic redistribution |
| SDXL 1.0 | Compatibility | T2I, I2I | CreativeML Open RAIL++-M | Eligible with license preserved |
| SDXL Inpainting 1.0 | Compatibility | Inpainting | CreativeML Open RAIL++-M | Eligible with license preserved |

Default image choice: **FLUX.2 [klein] 4B**.

Reason:
- high output quality;
- unified generation/editing;
- consumer-GPU oriented 4B model;
- official Apache-2.0 weights;
- supported by Diffusers and ComfyUI.

FLUX.2 [dev] remains selectable as a maximum-quality 32B FLUX option, but its gated weights use the FLUX [dev] Non-Commercial License and are therefore never automatically mirrored.

Qwen-Image-2.1 remains selectable as another quality-first option but its weights are not automatically mirrored by DLSS NR Manager because they use the Qwen Research License.

### Video

| Model | Tier | Tasks | License | Manager-owned redistribution |
| --- | --- | --- | --- | --- |
| Wan2.2 TI2V 5B | Recommended | T2V, I2V | Apache-2.0 | Eligible |
| Wan2.2 T2V A14B | Maximum Quality | T2V | Apache-2.0 | Eligible but very large |
| Wan2.2 I2V A14B | Maximum Quality | I2V | Apache-2.0 | Eligible but very large |
| Wan2.2 Animate 14B | Maximum Quality | V2V / animation | Apache-2.0 | Eligible but very large |
| LTX-2.5 Pre-Trained | Maximum Quality / advanced | T2V, I2V, V2V | LTX-2.x Community License | Manual license acceptance |

Default video choice: **Wan2.2 TI2V 5B**.

The A14B models remain available for quality-first jobs where VRAM, disk and latency are acceptable.

LTX-2.5 is exposed in the catalogue because it is a strong advanced production/video option, but DLSS NR Manager must not automatically redistribute the weights without validating/accepting the LTX community license.

## Backends

### ComfyUI

Role: primary workflow orchestrator.

Use cases:
- complex node graphs;
- inpainting/outpainting workflows;
- model-specific optimized community workflows;
- multi-stage video pipelines;
- local API/job execution.

License: GPL-3.0.

Policy:
- keep ComfyUI as a separate external process/runtime;
- do not statically link GPL code into the MIT DLSS NR Manager executable;
- preserve corresponding source and GPL notices when redistributed.

Pinned source:
- repository: `Comfy-Org/ComfyUI`
- ref: `52f98af2e2e42c421070a3e147c161c47cdeaf22`

### Diffusers

Role: direct/fallback Python pipeline.

Use cases:
- reproducible model-specific pipelines;
- simpler T2I/I2I/T2V/I2V execution;
- testing without a full ComfyUI graph.

License: Apache-2.0.

Pinned source:
- repository: `huggingface/diffusers`
- ref: `122b1e11fd497c3eeef14b3b98ca26a60166e48b`

## Pinned model/reference sources

Permissive source mirrors prepared in `third_party/DEPENDENCIES.lock.json`:

- ComfyUI
- Diffusers
- FLUX.2 reference implementation
- Wan2.2 reference implementation

They are group `ai-studio` and are only imported when explicitly requested:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\vendor-third-party.ps1 `
  -IncludeAiStudioSources `
  -StageImported
```

Normal application builds do not automatically clone these repositories.

Qwen-Image-2.1 and LTX-2 source/model materials remain manual until their licenses are explicitly accepted and redistribution policy is decided.

## GitHub Releases / large model packaging

Large AI runtimes and model weights are distributed through **DLSS NR Manager GitHub Releases**, not Git history.

There is **no DLSS NR Manager total size limit** for an AI Studio package. A model/runtime can be tens or hundreds of gigabytes.

GitHub currently requires each individual Release asset to be smaller than 2 GiB, so the transport layer automatically splits one package into GitHub-compatible parts (default ~1900 MiB), publishes a manifest with SHA-256 for every part, then reconstructs and verifies the original ZIP locally.

Example:

```text
ai-studio-wan2.2-ti2v-5b-<version>
├── wan2.2-ti2v-5b.manifest.json
├── wan2.2-ti2v-5b-<version>.part001
├── wan2.2-ti2v-5b-<version>.part002
├── ...
└── wan2.2-ti2v-5b-<version>.partNNN
```

The reconstructed package size is stored in the manifest and is not capped by DLSS NR Manager.

If a future package ever needs more than GitHub's per-release asset-count capacity, the same manifest format can span multiple package releases without changing the installed model layout.

## Offline runtime design

Target packaging architecture:

1. manager-owned Python runtime archive;
2. manager-owned wheelhouse with exact Python package hashes;
3. pinned PyTorch build compatible with the selected NVIDIA driver/GPU;
4. CUDA userspace libraries from the PyTorch wheel/runtime, without installing a system CUDA Toolkit;
5. vendored ComfyUI and Diffusers source snapshots;
6. model bundles or model manifests with SHA-256;
7. workflow bundles with versioned manifests.

Production/offline mode must set or enforce equivalent isolation semantics:

- `PYTHONNOUSERSITE=1`
- no global pip installs;
- no global Python mutation;
- no global CUDA Toolkit mutation;
- `HF_HUB_OFFLINE=1` when running a fully cached job;
- package installation from manager-owned wheelhouse only;
- model loading from the manager-owned/local models directory only.

## Current implementation status

Implemented now:

- Local AI Studio navigation entry.
- Create / Model Manager / Jobs / Runtime tabs.
- task-filtered multi-model selector.
- quality tiers.
- license and redistribution visibility.
- backend selector: Auto / ComfyUI / Diffusers.
- source image/video selection.
- inpainting mask selection.
- output folder selection.
- persistent local job manifests.
- isolated workspace creation.
- runtime/model status inspection.
- explicit source lock entries for ComfyUI, Diffusers, FLUX.2 and Wan2.2.
- upstream monitoring policy for the permissive source repos.
- automated catalogue tests.

Not yet claimed as complete:

- manager-owned Python archive.
- manager-owned PyTorch/CUDA wheelhouse.
- actual ComfyUI server launch/API client.
- actual Diffusers job executor.
- automatic model-weight installation.
- generation progress/cancel/retry.
- preview gallery.
- VRAM-aware scheduling.
- workflow editor/import/export.
- release assets for the isolated AI runtime.

These are intentionally kept separate from the first UI/catalogue change so large binary/runtime promotion can be audited and tested before production use.

## Quality-first recommendation

Image:
1. FLUX.2 [klein] 4B — default.
2. Qwen-Image-2.1 — optional maximum-quality/research-license path.
3. FLUX.2 [dev] — optional maximum-quality/gated non-commercial path.
4. SDXL / SDXL Inpainting — mature compatibility fallback.

Video:
1. Wan2.2 TI2V 5B — default practical local profile.
2. Wan2.2 A14B — quality-first optional profiles.
3. LTX-2.5 — advanced optional path after license acceptance.

Do not automatically choose the largest model solely because it is larger. The scheduler should eventually consider available VRAM, required resolution/duration, task type and desired quality tier.


## Models intentionally not enabled by default

### HunyuanVideo 1.5

Not enabled in the default manager catalogue/distribution policy.

Reason: the Tencent Hunyuan Community License for HunyuanVideo 1.5 explicitly excludes the European Union, United Kingdom and South Korea from its licensed territory. DLSS NR Manager is distributed generally, so automatically bundling this model would create territory-dependent licensing problems.

It can be reconsidered only as a manually supplied local model with explicit territory/license checks; it must not be promoted as a manager-owned global release asset.

### CogVideoX / older video stacks

Not selected as the default quality path. They may be added later as compatibility options if a concrete workflow needs them, but the default catalogue prioritizes current Wan2.2/LTX paths and avoids unnecessary runtime/model duplication.


## Download Manager

DLSS NR Manager exposes a dedicated **Téléchargements** menu for local manager-owned components.

Current managed entries include:

- Media Neural Runtime (video2dlssnr + FFmpeg)
- Real-ESRGAN AI Upscale
- all AI Studio model catalogue entries

Actions:

- **Installer**: install the selected component when manager-owned redistribution is allowed.
- **Supprimer**: remove the local copy while keeping the component available for later installation.
- **Retélécharger**: delete the local copy then perform a clean reinstall.
- **Annuler le téléchargement**: cancel the active download/install operation.

The menu is intentionally extensible so VLC and future Python/PyTorch/ComfyUI runtimes can be registered in the same centre after their feature branches are merged.

### Large-download approval

Any manager-owned package whose **total reconstructed size exceeds 1 GiB** requires explicit Yes/No approval before downloading.

For chunked AI Studio models, the approval uses the complete package size, not the size of an individual GitHub transport part. A 34 GiB model therefore shows one 34 GiB warning before the first chunk starts.

### Global progress indicator

The lower-left sidebar contains a compact global progress panel that is hidden when idle and displays:

- current download/component name;
- percentage when total size is known;
- transfer speed in MiB/s (displayed as Mo/s);
- estimated remaining time.

This progress hub is shared by manager-owned downloads so future model/runtime installers use the same UI.


## Manual-license installation flow

Models with restricted/gated licenses remain visible and installable through the central **Téléchargements** menu, but they are not mirrored automatically in manager-owned Releases.

Current manual-license models:

- FLUX.2 [dev]
- Qwen-Image-2.1
- LTX-2.5

When the user clicks **Installer**:

1. DLSS NR Manager opens a dedicated license window.
2. The exact model/license name and a short restriction summary are shown.
3. **Voir la licence officielle** opens the official license page.
4. The user must explicitly check that they have reviewed the license.
5. **Refuser** cancels with no model installation.
6. **Accepter** stores a local acceptance record containing model id, license name, official license URL and acceptance timestamp.
7. The application then asks for the folder containing files obtained from the official source.
8. Those files are imported atomically into the managed AI Studio model directory with progress/cancel support.

Acceptance is tied to the license identity. If the configured license name or official license URL changes later, the existing acceptance no longer matches and the dialog is shown again.

Accepting a model license does **not** give DLSS NR Manager new redistribution rights. Restricted weights are still not automatically copied to project-owned GitHub Releases solely because the end user accepted the license.
