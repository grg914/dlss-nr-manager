# Real-ESRGAN OpenMP-free evaluation (NOT for production)

Issue [#79](https://github.com/grg914/dlss-nr-manager/issues/79) records that the production `runtime-refresh.yml` workflow currently copies `vcomp140.dll` from Windows System32 into a package. **This experimental build does not fix production packaging and must not be published until independently validated.**

The vendored root Real-ESRGAN CMake enables `find_package(OpenMP)`, while its vendored ncnn also enables `NCNN_OPENMP` by default. Both must be disabled to evaluate a build that does not link OpenMP.

From a Windows x64 Visual Studio Developer PowerShell with pinned Vulkan SDK / vendored sources:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build-realesrgan.ps1 -BuildPath build-realesrgan-no-openmp -Clean -DisableOpenMp
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\audit-realesrgan-openmp.ps1 -ExecutablePath .\build-realesrgan-no-openmp\Release\realesrgan-ncnn-vulkan.exe
```

The PE import audit deliberately fails if it cannot locate `dumpbin.exe`, parse dependencies or sees a direct `vcomp140.dll`, `libomp.dll`, `libgomp.dll` or `libiomp*.dll` import. **It checks direct imports only**; transitives, signatures and every shipped DLL must still be audited.

Before proposing promotion, compare the current build and the experimental one on the **same Windows/NVIDIA PC** with x2/x4 models, representative images/video, Vulkan GPU selection, peak VRAM/RAM, throughput, quality and overnight stability. Record the exact NVIDIA driver, Windows build, GPU, SDK, commit, compiler, artifact hashes and performance. If the no-OpenMP version is inadequate, resolve the dependency by sourcing a properly licensed Microsoft Visual C++ Redistributable with documented provenance rather than copying a random System32 DLL.

**No code in this experiment changes production release defaults, runtime manifest, license notices or published assets.** #79 remains open until complete checks and explicit adoption.
