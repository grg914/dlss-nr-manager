# V4 — Real-ESRGAN OpenMP removal (#79)

Decision 2026-10-10: owner requested immediate retirement of OpenMP and `vcomp140.dll` from **Real-ESRGAN/NCNN only**. The build always passes `NCNN_OPENMP=OFF` and `CMAKE_DISABLE_FIND_PACKAGE_OpenMP=TRUE` and retains Vulkan. There is no opt-in build path to re-enable OpenMP in the manager script.

First Windows CI run: native compilation completed successfully and generated a 6,369,792-byte executable. The subsequent import audit failed because CI did not resolve the Visual Studio x64 `dumpbin.exe`, **not** because the executable failed to compile. This PR updates the audit to discover the x64 native toolchain and requires a fresh passing audit on the new head before any promotion.

The old Visual Studio OpenMP REDIST packaging branch is removed. No Microsoft `vcomp140.dll` is copied into the new package, and any direct PE imports of `vcomp*.dll`, `libomp*.dll`, `libgomp*.dll`, `libiomp*.dll` fail the production/CI audit. Windows `System32` and unrelated components are untouched.

**Release boundary:** workflow changes are proposed in draft PR #435, not merged/released here. Existing immutable v3.2.0/seed assets remain preserved, even if older binaries have different native dependencies. A new manager-owned runtime is allowed to stage only after Windows Vulkan/RTX evidence and operator variable `DLSSNR_REALESRGAN_RUNTIME_APPROVED=1`. Source/size/SHA-256, PE audit, x2/x4 image quality, GPU/VRAM, and process-cleanup checks are required. Actual consumer pin promotion remains a separately reviewed PR; issues #79 and #95 remain open.
