# v4.5 — ComfyUI local API groundwork (GPT C)

Status: experimental protocol adapter, **not production inference**. This work builds on signed Draft #310 and is deliberately separate from GPT B's v4.0 release and GPT A's rendering experiments.

## Reuse, not reimplementation

- Reuse the existing AI Studio model catalogue, job queue, license acknowledgement and managed Download Center. Do not create a second installer.
- Reuse the read-only runtime-integrity verifier and numeric HTTP loopback endpoint policy already present in #310.
- Upstream ComfyUI accepts an API-format workflow at POST /prompt (returns prompt_id) and exposes GET /history/{prompt_id}. The isolated AiStudioComfyUiApiClient supports just these calls. MainWindow and the existing Jobs queue do not call it. No Python launch, automatic job, installation, deletion or interruption.
- The offline protocol accepts only a small provisional core-node subset (128 KiB max, 64 nodes), excluding arbitrary custom/remote nodes, checkpoint/pickle loaders and unknown metadata. This subset is NOT a model-specific signed workflow or runnable-graph approval.
- The transport uses numeric loopback only, no HTTP proxy/cookies/redirects, bounded response, timeout and cancellation. It does not log prompts or response bodies. History cannot claim completion until the requested prompt has completed=true and status_str=success. No /interrupt call yet: without process ownership it could kill another job.

## Hard blockers to first real inference (issue #285)

1. Approve an immutable Windows x64 runtime lock with exact Python build and SHA-256, PyTorch/CUDA wheelhouse and transitive hashes, pinned ComfyUI version, compatible licensed model/weights and redistribution rights. The existing ComfyUI/Diffusers SOURCE pins are not proof of binary approval.
2. Import approved packages via the existing centralized Download Center and transactional rollback, using independently signed manager-owned release pins. The read-only manifest file verifier alone never authorizes execution.
3. Launch exactly one manager-owned Python process via ExternalProcessTracker, with private working directory, controlled environment, numeric loopback bind, no global Python/CUDA/pip, no custom nodes, and owned process-tree cancellation/cleanup.
4. Review an exact model-specific workflow/node/checkpoint allowlist, file hashes, Safetensors vs pickle rules, license acceptance and VRAM budget. Then wire an explicit Run action into AI Studio. Add job-specific progress, cancellation, recovery/output retention and FR/EN UI.
5. Validate xUnit/failure injection, exact-head signed Build/Analyze C#/CodeQL, real Windows 11 RTX 5060 Ti tests, process failure/recovery and legal sign-off before v4.5 production promotion.

No fork required: reuse upstream Comfy-Org/ComfyUI's API and existing pinned source until a separately reviewed upstream update is approved.

## Validation

Target the existing Windows .NET 8 offline CI and synthetic HttpMessageHandler tests. These fixtures never start ComfyUI, load models or access a GPU. This feature does not alter GPT B's main/Download Center or GPT A's Caustica source.

Official API references:
- https://github.com/Comfy-Org/ComfyUI/blob/master/script_examples/basic_api_example.py
- https://github.com/Comfy-Org/ComfyUI/blob/master/script_examples/websockets_api_example.py
