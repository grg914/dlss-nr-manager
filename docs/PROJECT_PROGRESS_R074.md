# R074 — Preserving PR #134 AI Studio updates when reconciling junction guard #135

**Protected main** `a1cec1242c246aa4cc167c0fed470545555f54eb` includes PR #134's receipt-backed manager-owned AI Studio optional-update code, 136 passing xUnit tests, and previous journal checkpoints R071/R070/R069/R064B/R064/R065. Historical PR #135 head `385961e32a81aba992e4048cfc6f30b63b195a70` predates the feature and must **never overwrite that main source file wholesale**.

This commit overlays **only** the five path-safety additions from PR #135 onto the exact new main `Services/AiStudioPackageService.cs`, carries the existing two symlink-sentinel tests, and preserves both PR histories as a two-parent merge on #135's branch (no force push). AI Studio receipt, version parser, transactional model update and central Download Center UI remain in place.

Current branch code adds fail-closed workspace/model/download/staging path checks before extraction and the manager-owned safe-removal guard for recursive cleanup. This guards extant junctions; handle-based TOCTOU, power-loss recovery and physical Windows/NVIDIA tests still block issue #95 closure.

**Do not merge until latest Windows Build, all regression tests and CodeQL succeed.** No version 4.0 release authorized.
