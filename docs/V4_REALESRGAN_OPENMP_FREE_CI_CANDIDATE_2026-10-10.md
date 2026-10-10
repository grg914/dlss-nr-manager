# V4 — OpenMP-free Real-ESRGAN CI candidate (#79)

Date: 2026-10-10. Status: ISOLATED CANDIDATE, not released.

A new read-only Windows CI workflow builds the locked Real-ESRGAN/ncnn source with both OpenMP paths disabled. It verifies the CMake cache, audits direct DLL imports for every EXE/DLL in the assembled candidate package using dumpbin, and reports the candidate SHA-256 and byte count in CI. Any build or import audit failure blocks this qualification. No runtime package, executable or Microsoft DLL is uploaded or published by the workflow.

Remaining release gates: inspect transitively and dynamically loaded native libraries, verify GPU Vulkan workloads (x2/x3/x4) and model quality/performance on the intended Windows RTX PC, record RAM/VRAM and process cleanup, preserve licenses, use manager-owned immutable package identifiers and verified SHA-256 receipts, and separately approve an actual new runtime through standard protected PR/CI. This candidate does not modify any production downloader or versioned release. Issues #79, #95 and #111 remain open; the existing license-redist gate must remain fail-closed.
