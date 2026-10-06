# Repository guidance

- Keep planning documents and `todos.md` local-only. Do not add `docs/*PLAN*.md` or `todos.md` to Git.
- Comments and Javadocs must describe the current implementation and its active invariants only.
- Do not preserve implementation history, migration notes, completed phases, or superseded behavior in source comments. Git history owns that context.
- Do not reference internal plan steps, phase labels, milestone IDs, or numbered design-document sections from source comments.
- Prefer direct explanations of why the current code is required, especially API contracts, synchronization rules, units, and non-obvious constraints.
- Until release, modify only the English locale (`en_us.json`); leave every other locale unchanged.
- Build release JARs locally on the user's Windows workstation with the required `DLSS_SDK`, `VULKAN_SDK`, and Slang environment configured. Rebuild `build/cmake/ngx_shim` in `Release` before Gradle because Gradle only bundles the existing native DLL; verify the embedded DLL and runtime-tested JAR hashes before uploading that exact JAR to the GitHub Release.
- GitHub Actions CI is manual-only (`workflow_dispatch`). Do not add push or pull-request triggers, and do not wait or poll for cloud CI during normal delivery unless the user explicitly asks for that run and its result.
- Automate the PrismLauncher instance with one correctly quoted argument string: `Start-Process -FilePath $prism -ArgumentList '--launch "26.2 rtx"' -WindowStyle Hidden`; use `--launch "26.2 rtx" --offline ArieX3` only as the local profiling fallback when launcher OAuth is unavailable. Never split the instance name across arguments, invoke Prism help, or use an instance `WrapperCommand` for profiling; those paths create intrusive launcher dialogs. Keep `OverrideCommands=false` and `WrapperCommand=` when finished.
- If the user gives a new durable rule about code style, structure, or process, add it here concisely when they explicitly ask to preserve it; do not record situational agreements from the current task.
- "Working" is not enough. After reaching a working result, remove temporary scaffolding, stale fallbacks, and misleading comments so the implementation is cleanly integrated.
- This repository is public. Never commit or push secrets, credentials, SDK keys, dumps, or other sensitive local material, and never print secret values in logs or responses.
