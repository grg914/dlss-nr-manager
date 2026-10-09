# AI Studio v4.6 — reviewed FLUX.2 Text-to-Image submission

**Experimental GPT C phase-2 preparatory work only.** Does not launch
ComfyUI/Python, send HTTP, download packages, accept foreign workflows
or authorize actual RTX inference.

`AiStudioComfyUiProtocol.TryCreateReviewedFlux2TextSubmission` takes bounded
typed prompt/settings, delegates exclusively to the pinned
`AiStudioFlux2Fp8WorkflowService`, and wraps its fixed 13-node FLUX.2
Text-to-Image graph in the ComfyUI `{"prompt": {...}}` API envelope.
It never accepts caller-supplied `class_type` / arbitrary node JSON.
The generic `TryCreateSubmission` allowlist for external graphs remains
unchanged and rejects this complex graph.

Statically inspected ComfyUI official `v0.39.0` code:
- `nodes.py`: UNETLoader, CLIPLoader (type flux2), VAELoader,
  CLIPTextEncode, ConditioningZeroOut, VAEDecode, SaveImage.
- `comfy_extras/nodes_flux.py`: EmptyFlux2LatentImage,
  Flux2Scheduler with steps, width and height.
- `comfy_extras/nodes_custom_sampler.py`: RandomNoise,
  KSamplerSelect, CFGGuider, SamplerCustomAdvanced.
- `comfy_extras/nodes_edit_model.py`: ReferenceLatent for later I2I.

Node identifiers/declared inputs match the builder as inspected; this does
**not** prove an image executes correctly, a model is legally redistributable,
GPU suitability or secure process lifecycle.

Hard gates before *any* real submission:
1. Independently pinned and approved full runtime closure from issue #285.
2. ComfyUI model search paths fixed and validated for the installed files.
3. Owned local process and loopback-only endpoint with version and runtime
   provenance check, no external plugin or cloud nodes.
4. Actual offline Windows 11 RTX 5060 Ti 16 GB functional validation.
5. Exit/stop/cancel/result/history API checks and license/NOTICE review.

This PR is stacked on experimental v4.6 job-lifecycle PR #365; keep Draft,
never merge directly into stable `main`. Source and tests do not modify
or install any third-party dependency.
