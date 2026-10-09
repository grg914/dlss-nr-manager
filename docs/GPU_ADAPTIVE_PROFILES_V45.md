# GPU-adaptive profiles — v4.5 draft (GPT A)

Status: **experimental draft, not in the published application**. Coordinate with GPT B's v4 installer fixes before any protected merge.

## Scope

The manager reads the installed NVIDIA GPU, generation, driver and available VRAM (when `nvidia-smi` reports it), and computes local recommendations. A fixed RTX 5060 Ti + Ryzen 9700X match is no longer offered in the v4.5 UI. Legacy persisted choices are migrated to AUTO without upgrading detected hardware capabilities.

- **AUTO** (default): capability-safe requests; quality/balanced/performance recommendation; NR OFF until explicitly requested in Manual.
- **Manual**: save SR, FG, Reflex, NR checkboxes as user preferences; always clamp to real GPU capabilities.
- **Compatible**: conservative SR/Reflex only, FG and NR disabled irrespective of saved manual flags.

The SR Quality/Balanced/Performance text is **advisory**, not a game graphics-setting API. MFG capability is reported, not toggled in an unsupported game. Game, runtime and driver preflight must still authorize any requested feature. The manager does **not** change NVIDIA Control Panel, NVIDIA App, NVIDIA Profile Inspector, global driver profiles, clocks, voltage, VRAM allocation or Windows graphics settings.

## Ownership, coordination and release gates

- **GPT A** owns this isolated pure recommendation service, persistence migration, localized Hardware & Profiles UI, tests and documentation in `feature/gpt-a-v4.5-adaptive-gpu-profiles-20261009`.
- **GPT B** owns the stable v4.0 `Install_Click` / runtime-staging P1 fixes. This isolated v4.5 draft adds a minimal fail-closed selection gate in `CaptureGameNvidiaSelection` and `Install_Click` to prevent the new AUTO/Manual/Compatible mode from being bypassed. **GPT B must review and reconcile that overlap with the final signed stable installer changes before integration**; do not overwrite B's branch.
- **GPT C** owns unrelated AI Studio v4.5 stacked PRs and should not edit these GPU-profile files concurrently.
- Preserve the stable release, existing security checks, source locks, external download behavior and legacy rollback/installer tests.

**Verification:** GitHub Build/xUnit/CodeQL on the final draft head to be recorded after runs. No physical Windows RTX, per-game frame pacing, VRAM budget control or panel-of-control integration is claimed. No release or protected merge authorized.
