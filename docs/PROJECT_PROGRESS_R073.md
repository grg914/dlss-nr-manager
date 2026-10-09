# R073 — Fail-closed AI Studio model staging/reparse protection (2026-10-09)

Issue #95: discovered a gap between lexical `ValidateModelInstallTarget` and later transactional swap safety; extraction into `installPath.staging-<GUID>` could be redirected through a junction under the model workspace **before** the transaction guard executes. Cleanup also recursively deleted temp directories without calling manager-owned reparse protection.

This draft PR:
- validates workspace and intended download root before creating folders;
- rejects symlinks/junctions in the exact selected model target before extraction, and also checks the staging directory before `SafeZip.Extract`;
- blocks cleanup of paths redirected via symlinks/junctions and logs skipped unsafe cleanup;
- adds xUnit sentinel-file tests for direct and parent symlinks where Windows CI host permissions allow them, preserving unrelated data.

**Scope and limits**: this is a best-effort preflight guard against existing reparse redirects, not an atomic handle-based defense against a concurrent adversarial rename (TOCTOU). Full Windows crash/cancellation, model import and multi-instance acceptance remain required. Build and CodeQL must be green on the final reconciled head before merge.

This branch starts at main immediately after PR #132, while PR #134 is open and also changes `Services/AiStudioPackageService.cs`; merge **#134 first**, then reconcile R073 on that main to retain the package-receipt and version-update implementation and all progress history. Do not force push over a concurrent contributor.
