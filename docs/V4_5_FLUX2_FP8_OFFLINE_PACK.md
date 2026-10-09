# v4.5 — FLUX.2 Klein FP8 verified offline-pack pipeline (GPT C)

Status: **experimental source**, no release publication or real GPU validation. This PR only targets the isolated GPT C v4.5 branch.

## 1. 7-Zip multipart extraction to manager-owned ZIP64

The owner's GitHub Draft Release `ai-studio-assets-staging-20261009` has 13 immutable size/SHA256 asset records in `manifests/ai-studio-staging-assets-20261009.json` (#330). Its custom `.7z.001` volumes **cannot** be handed directly to the existing model installer, which only understands ZIP64 `.partNNN` byte chunks + `<package-id>.manifest.json`.

A manually run, nonpublishing script `tools/prepare-flux2-fp8-offline-package.ps1` takes **the original 13 local files** (the Draft Release is not a public download source):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\prepare-flux2-fp8-offline-package.ps1 -AssetsDirectory "C:\path\to\13-staged-assets"
```

It verifies **every** uploaded file against the draft GitHub size/digest lock, verifies the official 7-Zip executable hash, lists each 7-Zip archive before extraction, accepts only an expected simple root filename, extracts in a dedicated build-local temporary directory, and verifies these **original** upstream Hugging Face model SHA256 values:

| Exact path after extraction | Upstream SHA256 |
|---|---|
| `diffusion_models/flux-2-klein-4b-fp8.safetensors` | `97ed34fe0567e436200f2faee3939b88f2b5d99f8af2a4dc16532c4245c0ccb6` |
| `text_encoders/qwen_3_4b.safetensors` | `6c671498573ac2f7a5501502ccce8d2b08ea6ca2f661c458e708f36b36edfc5a` |
| `vae/flux2-vae.safetensors` | `868fe7b343cc8f3a19dbcfcafbc3d5f888802be3f89bd81b65b3621a066ce8f3` |

The model references originate from `black-forest-labs/FLUX.2-klein-4b-fp8` and `Comfy-Org/vae-text-encorder-for-flux-klein-4b`. The separate UI workflow JSON SHA256 values were also checked against the immutable `Comfy-Org/workflow_templates` SHA `8be1f8c4b5af2d550d70922a23b79cee599e1f3e`.

Only after all checks pass, reuse `tools/publish-ai-studio-package.ps1` **without** `-Publish`. It writes the **local ZIP64 source archive, raw `.partNNN` files and manifest** under `build-local/ai-studio-packages/flux2-klein-4b/0.1.0`. It does NOT publish anything and does NOT automatically install ComfyUI.

The original downloaded 7-Zip volumes remain unchanged. Allow sufficient free disk space for both model extraction and the reconstructed ZIP64 package.

## 2. Bounded ComfyUI API graph generation

`AiStudioFlux2Fp8WorkflowService` constructs JSON **API node graphs** for text-to-image and one-image editing using core node types and exact FP8/encoder/VAE file names. Prompt, seed, resolution and image filename are bounded. The graph mirrors the core sampler/reference-latent structure of the pinned ComfyUI UI examples; the UI JSON is **not** passed to the API as-is. Test fixtures confirm fixed nodes, model selection, prompt escaping and refusal of unsafe external paths.

**Not execution authorization:** no runtime Python launch, ComfyUI upload-to-input bridge, job-queue/localhost submission, dynamic custom nodes or RTX benchmark is present. The existing generic local API client only accepts its narrower provisional allowlist. A signed and approved process/job runner must explicitly wire a reviewed graph variant after versioned ComfyUI node/API conformance and GPU tests. The new graph service alone cannot generate an image.

## 3. Download Manager / user choice

The existing Download Manager already lists AI Studio models and supports opt-in model install, removal, re-download, large-transfer confirmation, checksum/progress and manager-owned published package discovery. The new **Importer pack FLUX.2 hors ligne (ZIP64 vérifié)** button is shown only when the FLUX.2 model row is selected. It opens a file picker for the prepared `flux2-klein-4b.manifest.json` and shows the package's exact total size before >1 GiB confirmation. There is **no background download**.

When signed compatible online packages are ultimately **approved and published** under the expected `ai-studio-flux2-klein-4b-<version>` tag with `flux2-klein-4b.manifest.json`, the existing GitHub Releases model downloader can use its exact same ZIP64 format. A **Draft** Release is intentionally ignored.

## 4. Offline install / rollback

`AiStudioFlux2OfflineImportService` reads the local manifest and files without network requests, restricts package identity to FLUX.2 FP8, checks part ordering, lengths and per-part SHA256, reassembles ZIP64 and verifies archive SHA256, uses the existing SafeZip extraction, and separately verifies the *three original official model hashes* and the two upstream UI workflow JSON hashes before replacing the old model through `ManagedComponentRedownload.ReplaceAsync`. Cancellation/verification errors leave an existing model untouched. Never write to system Python, CUDA directories or the existing ComfyUI runtime.

**Blocking items before standalone self-contained offline AI installer or claimed working inference:** license/NOTICE and redistributability review; ComfyUI portable archive inspected with a pinned private runtime integrity manifest; ComfyUI model search-path integration; approved job runner and strict loopback upload/history; tests using actual packaged binaries on Windows RTX 5060 Ti; GPU/performance/VRAM verification; physical rollback, crash and no-network acceptance. No draft resources are auto-installed or published. Issue #285 remains OPEN.

This file is deliberately a handoff for parallel GPT A/B/C, not permission to merge to protected `main`.
