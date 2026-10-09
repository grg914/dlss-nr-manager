# R071 — PR #132 merged; PR #134 synced with current main (2026-10-09)

- Verified PR #132 checks green at exact HEAD `00c36b1a9030350ebdd4b8e2550807387f443262`, squash-merged into protected main commit `e6d6556e60661ff9251378b1480b5626918ea2b4`.
- Runtime seed publishers serialize and skip identical NuGet assets, while OpenMP remains fail-closed without signed/approved REDIST provenance. Neither immutable seed publication (#123) nor legal right to redistribute OpenMP (#79) is proved by this merge.
- PR #134 is reconciled using a two-parent merge commit on its own branch; journal R070/R069 prepended to main history R064B/R064/R065. No force push or branch deletion.
- Branch divergence #124: old refactor contains identical full Git subtrees for video2dlssnr, NVIDIA Streamline, Caustica RTX, Real-ESRGAN as current main; FFmpeg differs materially and requires full upstream validation before any branch deletion or merge.
- Recheck fresh Build/CodeQL and model update tests for PR #134 on new commit. v4 not released; physical Windows/NVIDIA acceptance remains external.
