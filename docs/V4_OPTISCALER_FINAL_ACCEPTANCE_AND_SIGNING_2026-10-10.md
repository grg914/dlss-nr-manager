# V4 OptiScaler — final audit and owner release preparation

2026-10-10. Review target: PR #443. This is a trial candidate only, not a published V4 release. Do not overwrite old assets or cross-merge GPT A/C private V4.5/V4.6 branches.

## Verified original binary

Original public release: https://github.com/grg914/dlss-nr-manager/releases/tag/v3.2.0
Original asset: OptiScaler-NR-v0.7.7-pre0-vendored-win-x64.zip
Release ID 406116106; asset ID 620010799; 131421020 bytes.
SHA-256: 14aa35affd569579709b2705cfa3c2624971a3d9e8e238fe71139954e23e734b.
GitHub Windows retrieval and PE archive verification: https://github.com/grg914/dlss-nr-manager/actions/runs/38037938511 (SUCCESS; 24 ZIP entries and 2 x64 DLLs).

The temporary Actions artifact #11664800784 expires 2026-10-17 at 08:28 UTC; only the original published release URL can be used as a persistent download source.

## Source review checklist

- [x] Stable release selection requires exact original release ID/tag, asset ID/name/URL, byte size and SHA-256. Neither same version labels nor recent GitHub upload times establish binary identity.
- [x] Candidate identity must match both source policy constants and bundled JSON receipt, and GitHub must explicitly report draft=false and prerelease=false. On mismatch, offer no stable candidate.
- [x] Per-game preference records the exact archive URL. Older tag-only preference remains readable. A WPF ItemsSource refresh must not silently overwrite a saved selection.
- [x] Operator sees the source SHA-256 and is warned this binary is NOT hardware/game certified before installation. Anti-cheat safeguards remain in place.
- [x] Existing download SHA verification, ZIP protection, backup, transaction journal and rollback are retained. Pinned ZIP byte length is checked. The installation manifest includes source URL and SHA; old manifests still deserialize.
- [ ] The final exact Git SHA of the latest changes must have Build, CodeQL and original archive workflow green, with no new compiler warnings/errors from these changes.
- [ ] Actual Windows RTX and rollback acceptance, plus GitHub Verified owner signature.

## Operator-only physical Windows RTX acceptance — NOT performed by CI

Use a disposable supported SINGLE-PLAYER game installation without anti-cheat. Do not inject proxy DLLs into multiplayer or protected games.

- [ ] Record GPU model/generation, NVIDIA driver, Windows build, game renderer and version, test date, and pre-install file hashes.
- [ ] Compile/publish the reviewed PR on Windows; capture exact Git commit and executable hash.
- [ ] Select Stable -> OptiScaler original v3.2.0; confirm displayed SHA, explicit warning and operator consent; verify no same-version package is substituted.
- [ ] Install. Verify .dlssnr-manager-state.json SourceArchiveUrl and SourceArchiveSha256; compare installed proxy hash with OptiScaler.dll inside the verified ZIP.
- [ ] Test actual renderer initialization, OptiScaler loading, supported DLSS / FG / NR features, gameplay and clean process exit, with logs, CPU/RAM/VRAM observations.
- [ ] Test cancelled/corrupted download, killed app, restart recovery, collisions with user-owned files and transaction rollback in a disposable test folder.
- [ ] Use Restore Latest in the manager; compare before/after hashes and ensure original user files are preserved.
- [ ] Record French and English warning/errors, logs and pass/fail evidence in issues #95 and #161.

## Critical integration and owner signing order

PR #440 and #443 both modify MainWindow.xaml.cs, in currently distinct regions. Both still need signed integration. PR #441 is a separate native compiler reproducibility investigation.

1. After #440 passes its own acceptance/signature/review gates and merges into protected main, REBASE or carefully reconstruct #443 onto the NEW main, retaining ALL shutdown/cancellation changes from #440. Never perform a soft reset to a newer main BEFORE this rebase, because the #443 tree could revert #440.
2. Review the resulting full diff, re-run exact-head Build/xUnit, CodeQL and original asset qualification. Keep #443 draft until all mandatory evidence exists.
3. The owner alone configures local SSH/GPG signing; no private key or tokens are shared with agents. Use a NEW clone, verify main is an ancestor of the rebased feature, save the original HEAD SHA and full tree SHA, and create a ONE-COMMIT signed equivalent with git commit -S and reset --soft origin/main.
4. Check that the signed commit tree SHA EXACTLY equals the reviewed candidate tree SHA. Push with --force-with-lease against the saved HEAD to ONLY the unprotected PR feature branch, NEVER to main. If a concurrent agent pushed a new HEAD, stop and re-review.
5. Re-run all CI on the new signed exact head. Require GitHub signature status Verified and the configured protected PR reviews/checks. No automatic releases, tags, merges or asset overrides.

## Remaining release blockers

Issue #95: real device transactional rollback / restart / fault injection.
Issue #161: intermittent OptiScaler rebuild nondeterminism, MSVC C4744/LNK4098 and native loading. Re-using this already published binary avoids new recompilation for the trial but does not resolve source build issues.
Physical GPU function and anti-cheat-safe game compatibility are not certified by GitHub Actions.

No physical RTX test is asserted as passed, and no production publication is authorized here.
