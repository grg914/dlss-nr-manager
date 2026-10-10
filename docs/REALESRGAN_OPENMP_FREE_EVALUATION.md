# Real-ESRGAN / NCNN: OpenMP disabled by default (V4)

Decision (2026-10-10): the V4 build policy permanently disables both vendored ncnn OpenMP (`NCNN_OPENMP=OFF`) and root CMake OpenMP discovery (`CMAKE_DISABLE_FIND_PACKAGE_OpenMP=TRUE`). The Vulkan GPU path remains enabled. This setting applies **only** to the Real-ESRGAN native executable, not Windows, NVIDIA drivers, WPF, VLC, OptiScaler, AI Studio or other executables.

The historical OpenMP/Visual Studio REDIST branch is retired from the packaging workflow. No `vcomp140.dll` is copied from `System32` or `VC/Redist`, and the new package contains only the compiled executable and its upstream license notices. The Windows PE import audit fails closed if a direct OpenMP DLL import is present in any shipped EXE or DLL.

From Windows x64 with build prerequisites installed:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build-realesrgan.ps1 -BuildPath build-realesrgan-no-openmp -Clean
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\audit-realesrgan-package-openmp.ps1 -PackageDirectory .\build-local\realesrgan-no-openmp-candidate
```

The CI workflow `.github/workflows/realesrgan-openmp-free-qualification.yml` produces a Windows build and a direct PE import audit; it does **not** publish a release asset or validate actual NVIDIA/RTX GPU execution. Direct import inspection does not exclude dynamically loaded or transitive dependencies.

**Important:** existing published `v3.2.0` / `runtime-seed-v1` binaries are immutable historical assets and are not silently rewritten. The production runtime-refresh job requires `DLSSNR_REALESRGAN_RUNTIME_APPROVED=1` after actual Windows Vulkan/RTX acceptance before staging a new immutable package. The approved SHA-256 package must be promoted separately through reviewed consumer metadata; no automatic overwrite of the canonical runtime asset. Issues #79 (no-OpenMP proof), #95 (physical Windows validation) remain open until those checks pass.
