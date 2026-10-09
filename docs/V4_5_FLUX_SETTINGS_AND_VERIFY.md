# v4.5 AI Studio — image settings and explicit model integrity

This is **GPT C experimental** work stacked on FLUX.2 offline package PR #332, not on stable main. It does not approve a runtime, license or GPU execution.

## Image controls

For FLUX.2 Klein 4B only, and the image generation/editing task types already supported by its reviewed graph builder, the **Créer** tab now exposes:
- Resolution/aspect presets: 1024×1024 (1:1), 1024×576 (16:9), 576×1024 (9:16), 1024×768 (4:3), 768×768 (1:1).
- Steps: 4 (default), 8, 12, 20.
- Seed: **Auto** (generated once at queue time) or a positive decimal integer.

Only settings with 256–2048 pixel multiples of 16, at most 4,194,304 pixels, 1–32 steps and a bounded seed are accepted. The values are saved in the queued job JSON, displayed in **Jobs**, and used by `AiStudioFlux2Fp8WorkflowService` when an approved executor is eventually available. Existing queued job files remain readable without the optional `ImageSettings` field. **Queueing does not run ComfyUI.** No fake generation status, model or workflow activation is implied.

## Optional installed-model integrity check

An extra **Vérifier l'intégrité de FLUX.2 installé** button is visible in **Téléchargements** only when the FLUX.2 model directory is present. It requires explicit user action, uses local storage only and can be cancelled. It scans the full >12 GB installed model payload, compares the *original model* SHA-256 values to immutable upstream pins and both UI JSON fixture hashes, checks the manager-owned receipt schema/package identity and refuses unexpected/missing files or reparse points.

The local ZIP64 installer checks that the target drive has sufficient temporary headroom (ZIP archive + an explicit **14 GiB maximum expanded payload** + 1 GiB margin). `SafeZip` also rejects archives whose declared uncompressed size exceeds 14 GiB; it does not wait for disk exhaustion to detect a decompression bomb. The limits fit the pinned three FLUX.2/Qwen/VAE weights and the two small workflow references.

The check is intentionally **not** run at startup, on page selection or during background update checks. A positive result means the **file bytes match pinned model hashes**, NOT that the package distribution rights, ComfyUI runtime, GPU hardware or executable workflow have been approved. If the check fails, files are left intact for diagnosis; no silent repair is attempted. Previously installed model and queued jobs remain untouched.

## Still blocked before a real v4.5 release

1. Runtime ZIP/7z full private closure + independent signed root SHA/manifests and license/NOTICE review.
2. Validated Windows NVIDIA ComfyUI process ownership, strict isolation and loopback-only /prompt + /history + /view; GPU conformance for exact core nodes.
3. Explicit job Run/Cancel/status/result transactions; no automatic execution merely because files exist.
4. Optional online install from **published, authorized** GitHub Releases, plus preassembled no-network installer. Draft assets stay private.
5. Windows 11 RTX 5060 Ti 16 GB and truly disconnected PC functional/rollback/VRAM acceptance.
6. Final exact-head Build, xUnit, CodeQL, review and protected signed integration into approved target branch; no automatic stable main promotion.
