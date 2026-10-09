# v4.5 AI Studio — incomplete model directory detection

**Scope:** GPT C, on an isolated branch stacked on experimental #342. This fixes the concrete finding forwarded by GPT A. It does not change v4 stable or the restricted-weight licensing policy.

## Previous bug

`LocalAiStudioService.IsModelInstalled` previously returned `Directory.Exists(modelDirectory)`. An empty directory created by an interrupted import could be shown as installed in the AI Studio model panel and Download Manager, and erroneously enable model update actions.

## Corrected lightweight state machine

`InspectModelFiles(model, directory)` distinguishes:

- **Missing** — the model directory does not exist.
- **Incomplete** — directory exists but contains only metadata/README, is redirected/unsafe, lacks the mandatory FLUX.2 Klein FP8 three-weight file set, or lacks a valid manager-owned completion receipt.
- **FilesPresentUnverified** — required nonempty model files and (only for manager-owned models) the versioned local package receipt are present.

For FLUX.2 Klein FP8, require nonempty files at `diffusion_models/flux-2-klein-4b-fp8.safetensors`, `text_encoders/qwen_3_4b.safetensors`, and `vae/flux2-vae.safetensors`, plus a well-formed matching manager-owned receipt. For other model IDs, a bounded scan (max 2,048 entries, five subfolder levels; no symlink traversal) detects actual nonempty recognized weight files. Manager-owned models additionally require a valid matching package receipt. **Restricted-license/manual models do not require or acquire manager-owned receipts**; their manual import and license acceptance remains unchanged.

These checks intentionally inspect directory metadata only. They do not hash multi-gigabyte weights or equate file presence with trusted inference capability. The explicit **Vérifier l'intégrité de FLUX.2 installé** button remains the full SHA-256 step; the trusted runtime, license and GPU execution gates under #285 remain unchanged.

## User experience and recovery

AI Studio status and Download Manager distinguish incomplete files from unverified present files. Incomplete imports are **not** counted as installed and do not qualify for model updates or redownload. The explicit **Supprimer** action stays available for cleaning an incomplete managed folder, using the existing symlink-safe removal helper.

## Focused regression coverage

Synthetic temporary-directory tests cover missing/empty folders, config-only folders, interrupted FLUX.2 payload, zero-length required weight, missing/wrong receipt, complete nonempty FLUX.2 file set, and restricted/manual models without receipts. The tests do not download, publish or invoke AI weights or Python.

**Still blocked:** actual hash/size verification of staged 13 assets on Windows, independent runtime pin/NOTICE, controlled ComfyUI process and job execution, and offline RTX 5060 Ti acceptance. Keep staging Draft and this PR Draft until CI/review; never promote to protected main automatically.
