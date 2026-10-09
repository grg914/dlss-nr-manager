# Root-cause analysis — Real-ESRGAN v3.2.0 NCNN .param mismatch

**Date:** 2026-10-10. **Status:** ROOT CAUSE CONFIRMED BY RECONSTRUCTED SHA-256 AND ORIGINAL GITHUB RELEASE JOB; REMEDIATION NOT PUBLISHED. **Issues:** #411, downstream acceptance #95/#111, separate legal OpenMP #79.

## Evidence cross-checked

- Signed baseline prior to this investigation: main `8313565324659ba4a46b6c79ef0777ec36b8feea`. Current `AI_PROJECT_COORDINATION.md`, `AI_PROJECT_PROGRESS.txt` and `AI_PROJECT_PROGRESS2.txt` were read, including prior GPT A/B/C handoffs. They discuss model source pinning, legal OpenMP and immutable runtime publishing but did **not** identify the specific five-file CRLF drift.
- The original release workflow run [37696826691](https://github.com/grg914/dlss-nr-manager/actions/runs/37696826691) on 2026-10-07 (22:32–22:41 UTC), `Release`, completed **success**. Its job [113050507716](https://github.com/grg914/dlss-nr-manager/actions/runs/37696826691/job/113050507716), step **Bundle Real-ESRGAN model files from manager-owned source**, verifies `git hash-object -- $asset.Source` before `Copy-Item`. The log at 22:40:10 prints the exact CRLF-reencoded SHA-256 values below and then uploads the assets to stable release `v3.2.0`.
- The git tag `v3.2.0` points to `e5927e1d718dfff19540964ba46ee4edaa933c6b` and **does not contain a root `.gitattributes`**. The nested `third_party/Real-ESRGAN-model-sources/spintexture/.gitattributes` does contain `*.param binary`; the analogous `realesrgan-ncnn-py` source has no matching `.gitattributes`. The build ran on `windows-latest`, where implicit line ending conversion affected text-classified .param files.
- Root `.gitattributes` was **added later**, 2026-10-08 03:35 UTC, via signed commit `d0caf982692c73ea4759592d1a026ac9af0beb63` (#86), including `*.param binary`. This should protect *future checkouts*, but it cannot change already published immutable `v3.2.0` assets.
- Git `hash-object` without `--no-filters` may canonicalize CRLF into LF according to Git's text conversion policy, so the source integrity gate can pass **despite actual on-disk working-tree bytes differing from the expected raw blob**. A local Git experiment confirmed: after replacing LF with CRLF, `git hash-object -- file.param` still returned the LF blob, whereas `git hash-object --no-filters file.param` detected different raw bytes. `Copy-Item` then preserves the on-disk CRLF, and the later SHA256SUMS/publisher digest validates the **transformed** asset, not the original pinned blob.

## Exact reconstructed cryptographic proof

For each model, fetched canonical source bytes (Git `LF`), replaced each byte `0A` (LF) with `0D 0A` (CRLF), independently calculated SHA-256 in JS (tested against standard `abc` vector), and compared with immutable public GitHub Release asset `digest` and the original Release job log. **5/5 reconstructed SHA-256 values match exactly**. This proves the observed release asset bytes are consistent with line-ending conversion, without needing to download binaries.

| File | Source bytes (LF) | Source LF count | Published bytes (CRLF) | Published SHA-256 (equals reconstructed CRLF SHA-256) |
| --- | ---: | ---: | ---: | --- |
| realesrgan-x4plus.param | 116029 | 1001 | 117030 | `c8a066c12541a1ef01ba90fddd90688278bdc11536847699239be29225015bc8` |
| realesrgan-x4plus-anime.param | 30290 | 270 | 30560 | `d63c7e93c58ec5d0048ca1f0a995f40b2af17c2816a2e0a3b7172046d98795ec` |
| realesr-animevideov3-x2.param | 3173 | 43 | 3216 | `1393f7c0e885f9d15a0668329a13f695ebd0ea45791f46d28145d7934824d224` |
| realesr-animevideov3-x3.param | 3173 | 43 | 3216 | `584a43e429188c159ef8e42191ef5a5fd1d2e3b17398e5cee6089726dec879c7` |
| realesr-animevideov3-x4.param | 3077 | 42 | 3119 | `157c0a10405f885d4c53ce76fe2ba731694bcd16ec90ab6b889335e384239682` |

Control: `realesrnet-x4plus.param` from the spintexture vendor tree has the **same canonical Git blob** as `realesrgan-x4plus.param` (`d14d62ebb815bdd522ed112e67695b3377f86ca0`) but was published in its original LF form, 116029 bytes, SHA-256 `35330ececcea33b6c397a72548e788d5d53becee4734c50b7fada36e89f10a86`. The binary `.bin` models were not subject to this text newline transformation.

## Actual impact

`Services/AiUpscaleService.cs` pins 12 model assets to immutable Git blob SHA-1 and original LF byte sizes. `DownloadModelAsync` first compares the HTTP Content-Length and actual size, then hashes the downloaded file with Git blob SHA-1. The five transformed `.param` assets **will fail** this validation if downloaded from the public `releases/latest` endpoint while `v3.2.0` is latest. The affected fresh install/repair cannot be claimed working merely from green Build/CodeQL; Real-ESRGAN Vulkan and rollback tests still require Windows RTX evidence. No evidence here of intentional model manipulation; transformed files differ exclusively in newline encoding as certified by SHA-256.

## Correct fix and release policy

1. **Already on stable main:** root `.gitattributes` marks `*.param binary`; future clean checkouts should preserve exact source bytes.
2. **New isolated source PR:** `.github/workflows/release.yml` changes the release-stage source check to `git hash-object --no-filters`, then validates the raw destination blob **after** `Copy-Item` against the same approved pinned source. Prevents Git's filter-normalized digest from hiding line-ending conversions. Add xUnit coverage for six source snapshots, root attributes and workflow enforcement.
3. Validate the signed PR head on Windows Build, xUnit and CodeQL. At the next authorized release, verify every final `.param` SHA-256 and size **before upload**; check GitHub release asset digests after upload, local install, rollback and Vulkan RTX operation.
4. **Do not overwrite existing v3.2.0 release assets**. Publish corrected model bytes only as part of an approved, immutable new manager-owned release/channel with provenance and updated manifest if required. Keep pinned consumer sizes/blob IDs for the original models, never weaken checks or replace trusted source weights with the CRLF copies.
5. Keep **#411 OPEN** until a new approved asset set is published and fresh Windows Real-ESRGAN installation succeeds. This does not close #95 (device/transactions), #111 (component-version receipts), or #79 (legal Microsoft REDIST/alternative performance proof). No license flag, driver or public asset changed by this investigation.

## GPT A/B/C coordination status

GPT A's prior source/runtime publication and immutable-asset audit correctly identified asset churn and legal gating, but the documented component ZIP size/digest checks did not include per-model raw-line-ending conformity. GPT C's parallel V4.5 AI Studio/ComfyUI model work is unrelated to this V4.0 Real-ESRGAN issue. GPT B retains stable V4 release integration; only append the finding to shared journals, do not overwrite GPT A/C history or merge their experimental branches to resolve it.
