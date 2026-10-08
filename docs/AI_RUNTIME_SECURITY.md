# AI Runtime Security Policy

## Scope

This policy applies to AI Studio, ComfyUI, Diffusers, Python/PyTorch runtimes, custom nodes, workflows, local model imports and future local inference engines.

## Isolation

AI runtimes must remain application-private.

Required design goals:

- no modification of system Python;
- no global pip installation;
- no system-wide CUDA Toolkit mutation;
- no implicit PATH/PYTHONPATH pollution;
- private runtime, wheelhouse, models, workflows and outputs under application-managed storage;
- environment variables such as `PYTHONNOUSERSITE=1` when applicable.

## Python packages

Production/offline packages should come from an exact manager-owned wheelhouse/manifest.

Requirements:

- exact versions;
- hashes for promoted packages where practical;
- no silent `pip install latest`;
- no runtime fallback to PyPI when offline/manager-owned mode is expected;
- package upgrades go through review and regression tests.

## ComfyUI custom nodes

Custom nodes can execute arbitrary Python code with the user's privileges.

Therefore:

- do not auto-install arbitrary community custom nodes;
- do not run downloaded custom-node code merely because a workflow references it;
- manager-owned nodes require exact source revision, license and review;
- third-party nodes should require explicit user action and warning;
- prefer allowlisted/pinned node packages for supported workflows;
- never expose a ComfyUI API listener to LAN/Internet by default.

The default local API should bind only to loopback unless a future explicit remote-access feature is designed and secured.

## Model formats

Prefer data-oriented formats that do not execute arbitrary code while loading, such as:

- SafeTensors for compatible model weights;
- ONNX for supported inference paths.

Treat Python pickle-based formats (`.pkl`, many `.pt`/`.pth`/`.ckpt` workflows) as executable/untrusted input unless the exact source is trusted and reviewed.

Do not load arbitrary user-downloaded pickle model files automatically.

## Model import

For manager-owned models:

- verify package/chunk SHA-256;
- verify reconstructed archive SHA-256;
- use safe archive extraction;
- install atomically.

For restricted/gated user-supplied models:

- require the applicable license flow;
- import only from a path selected by the user;
- keep the import inside managed storage;
- do not infer project redistribution rights from user acceptance.

## Workflows

Workflow files are configuration, but may reference executable nodes/scripts.

Manager-provided workflows must:

- be versioned;
- identify required models/nodes;
- avoid hidden network fetches;
- fail explicitly when a dependency is absent rather than silently downloading code.

## Network

Local generation should not upload prompts, inputs or outputs.

Network access for model/runtime installation must use the canonical Download Manager or an explicit official-source/license flow.

## Process execution

External AI processes should be launched with:

- explicit executable path inside the managed runtime;
- explicit working directory;
- controlled environment;
- no shell interpolation of untrusted prompts/paths;
- cancellation and process-tree cleanup.

## Secrets

Hugging Face or other provider tokens must not be placed in:

- source control;
- workflow files;
- job JSON;
- ordinary logs;
- model manifests.

If token-backed gated downloads are added later, use an explicit secure credential design.

## Deletion

Removing an AI model/runtime should remove the application-managed local copy without deleting unrelated user files or generated outputs unless the user explicitly requests that.

## Review trigger

Any change adding:

- a new Python package;
- a new custom node;
- a new model format;
- a new executable;
- a network listener;
- a provider token;
- arbitrary script execution;

requires a security review before production promotion.
