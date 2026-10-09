# v4.5 — Offline audit of the official ComfyUI NVIDIA portable archive

**Experimental / inspection only.** No ComfyUI installation, Python launch, CUDA modification, network access, package publication or model inference is authorized.

The owner uploaded `ComfyUI_windows_portable_nvidia.7z` (official Comfy-Org v0.39.0) and `7zr.exe` (7-Zip v26.04) to the 13-file private Draft Release. Their GitHub release SHA-256 digests and exact byte sizes are pinned in `manifests/ai-studio-staging-assets-20261009.json`. The archive contains an independently embedded Python environment under `ComfyUI_windows_portable/python_embeded` and ComfyUI's `main.py`. See <https://github.com/Comfy-Org/docs/blob/main/installation/comfyui_portable_windows.mdx>.

## Operator-only dry run on Windows with local copies

Copy the 13 original Draft Release files to one local folder (do not rename or alter them), then from this project's root in Windows PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\inspect-comfyui-portable-offline.ps1 -AssetsDirectory "C:\path\to\original-13-files"
```

The script:
1. Checks the exact locked 7zr.exe and ComfyUI NVIDIA archive file sizes and SHA-256 before executing 7zr (never executes any extracted Python).
2. Inspects every 7-Zip member path, enforces the known portable root, rejects path traversal, drive/external paths and control characters, requires `python_embeded/python.exe` and `ComfyUI/main.py`, and caps reported expanded size at 24 GiB.
3. Verifies target free disk space, then extracts to a new **build-local** staging directory only; the active AI Studio installation remains untouched.
4. Rejects extracted reparse points, inventories every regular file's name, size and SHA-256, and writes `comfyui-portable-candidate-inventory.json` beside the extracted tree for offline dependency and third-party license review. A failed validation cleans up the attempted inspection.

**Do not promote this output** automatically as `AiStudioRuntimeIntegrityService`'s trusted manifest. The signed trusted pin must come from an independent approved manager-owned source, and current runtime verifier limits may not yet cover the entire portable Python wheel/torch dependency tree. Exact unpacked file count, Python/PyTorch CUDA versions, NVIDIA driver compatibility, NOTICE/licensing, hidden network calls/custom nodes and GPU acceptance are still unverified.

## Next integration gate (issue #285)

- Compare generated candidate inventory to an independently published, reviewed runtime closure and confirm all licenses and redistributed artifacts.
- Separate model weights from portable Python environment; set model path mapping only after verifying ComfyUI's `extra_model_paths.yaml` contract for the selected version.
- Create a verified, reversible installation path into the **manager-owned private runtime**. Never install global pip/Python/CUDA or execute the bundled .bat auto-updaters.
- Build a tightly controlled, process-owned localhost-only executor, workflow/model allowlist, cancellation/result handling, bounded IPC, job-state recovery and explicit first-run consent.
- Run Windows 11 disconnected and RTX 5060 Ti 16 GB tests. Do not ship an offline full installer or publish Draft assets until those gates pass.

Scope: GPT C v4.5 only, stacked on the verified 13-asset lock #330 / ZIP64 packaging #332. Main and GPT A/B stable/RTX work remain untouched.

## Windows PowerShell 5.1 — Torch long-path audit recovery

On Windows 11, the official archive passed size/hash and 7zr extraction, but the inventory failed at PowerShell 5.1 Get-ChildItem -Recurse on deeply nested PyTorch license paths (DirectoryNotFoundException). This is a script traversal problem, not evidence of an incorrect archive checksum.

The corrected inspector now uses .NET extended Windows paths (\\?\), rejects reparse-point entries before descending and hashes each file through a read-only stream. The two recursive Get-ChildItem calls have been removed. No administrator rights, PowerShell 7, registry edits or Python executions are required.

Existing local Git checkouts do not update automatically; update the branch after its exact-head CI finishes, then run:

```powershell
git -C "$env:USERPROFILE\Desktop\DLSSNR-Audit" pull --ff-only
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:USERPROFILE\Desktop\DLSSNR-Audit\tools\inspect-comfyui-portable-offline.ps1" -AssetsDirectory "$env:USERPROFILE\Desktop\AIStudio-Assets"
```

On any further error, preserve the output and do NOT execute Python or run_nvidia_gpu.bat. The SHA inventory remains CANDIDATE_ONLY, not a runnable or redistributable package.
