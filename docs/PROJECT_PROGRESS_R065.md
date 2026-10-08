# R065 — OpenMP redistribution release gate (2026-10-09)

Issue #79: upstream packaging of Real-ESRGAN copied arbitrary Windows System32 vcomp140.dll into the publicly downloadable manager runtime. Official Microsoft [Visual Studio 2026 redistribution guidance](https://learn.microsoft.com/en-us/visualstudio/releases/2026/redistribution) says only listed redistributable files may be distributed, subject to the license rights of the Visual Studio edition installed. A system directory is not evidence of the producer's redistributable source or license.

This PR removes that System32 copying path and adds a **fail-closed** step. The Real-ESRGAN release job will not publish until the operator explicitly attests redistribution approval via DLSSNR_OPENMP_REDIST_APPROVED=1. The helper accepts a Visual Studio x64 VC/Redist/MSVC file, disallows debug_nonredist directories, verifies the Microsoft Authenticode signature and exact SHA-256 both pre/post copy, and includes machine-readable version/source provenance.

The user/release operator must still demonstrate the actual Visual Studio license rights, verify the correct x64 OpenMP dependency imports on a real Windows target, and prove successful functionality/performance. No generic CI environment is authorized to assume legal permission. Issue #79 remains open until this evidence exists.

Branch tests are static guardrail checks only; GitHub Build and CodeQL and a real Windows redistributable provenance test remain mandatory before publishing. Root progress journal R065 written without historical truncation.
