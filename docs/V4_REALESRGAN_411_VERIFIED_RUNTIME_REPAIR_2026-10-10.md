# V4 — Real-ESRGAN #411: exact-byte historical repair

Date: 2026-10-10. **Status: candidate source correction, NOT released/physically qualified.**

## Cryptographic root cause — confirmed, not hypothetical

The original upstream source tree `Tohrusky/realesrgan-ncnn-py@900c0549a2fb3481b71d0369253522519308f1f2` contains five pinned NCNN text `.param` blobs. The public `grg914/dlss-nr-manager` `v3.2.0` GitHub release emitted these files after Git's Windows LF→CRLF working-tree conversion. We fetched the exact immutable upstream source blobs and verified SHA-256 by converting **every source byte 0x0A into 0x0D0A**; the five reconstructed digests match the published GitHub release digest fields EXACTLY (5/5). Therefore the published files represent only line ending conversions of the pinned sources, not changed model architecture/options/weights.

| File | Canonical bytes | LF count | Published CRLF bytes | Published SHA-256 |
| --- | ---: | ---: | ---: | --- |
| `realesrgan-x4plus.param` | 116029 | 1001 | 117030 | `c8a066c12541a1ef01ba90fddd90688278bdc11536847699239be29225015bc8` |
| `realesrgan-x4plus-anime.param` | 30290 | 270 | 30560 | `d63c7e93c58ec5d0048ca1f0a995f40b2af17c2816a2e0a3b7172046d98795ec` |
| `realesr-animevideov3-x2.param` | 3173 | 43 | 3216 | `1393f7c0e885f9d15a0668329a13f695ebd0ea45791f46d28145d7934824d224` |
| `realesr-animevideov3-x3.param` | 3173 | 43 | 3216 | `584a43e429188c159ef8e42191ef5a5fd1d2e3b17398e5cee6089726dec879c7` |
| `realesr-animevideov3-x4.param` | 3077 | 42 | 3119 | `157c0a10405f885d4c53ce76fe2ba731694bcd16ec90ab6b889335e384239682` |

Upstream source Git blob SHA-1 and expected LF sizes were already pinned in `Services/AiUpscaleService.cs`, and the Windows source checkout/release copier had previously been fixed by using `git hash-object --no-filters` plus root `.gitattributes` `*.param binary`. These actions alone cannot repair the existing public release, so first installs still failed before this candidate.

## Source-level remediation, without modifying the historical release

- The 12 model URLs point to an **explicit v3.2.0 release tag**, never a moving `/releases/latest/download` alias. The approved engine release API is also version-scoped; updating model binaries later requires separately approved changes and #111 optional Update receipts.
- `RealEsrganPublishedModelRepair` holds approved published **byte size and SHA-256 for all 12 public model assets** from GitHub release metadata. Downloaded bytes are first strictly size-limited and SHA-256 verified.
- Only the five explicitly documented CRLF-transformed, SHA-256-matching files may be rewritten in the **temporary download stage**. The repair rejects lone CR, lone LF and non-ASCII bytes, emits source LF and requires exactly the pinned canonical byte size.
- After the transformation, `AiUpscaleService.DownloadModelAsync` still requires its original immutable **Git blob SHA-1** for all 12 files. It atomically replaces a model destination only after download, public SHA-256, optional normalization, canonical size and raw blob integrity pass. On a mismatch, the staged `.download` file is deleted; the previously installed destination is unchanged.
- Existing 7 untransformed models remain byte-for-byte untouched and must match both the published SHA-256 and existing source Git blob SHA-1. This is a narrowly scoped compatibility repair, **NOT** disabling model hash checks, changing model options, repinning untrusted assets, or redistributing Microsoft OpenMP.
- `tools/verify-realesrgan-model-candidate.ps1` recognizes the explicit approved model URLs in the exact 12-source model records while still verifying original source-stage LF bytes with unfiltered Git blob SHA and SHA-256. Future new release candidates must validate their own original raw bytes and produced release receipts, without rewriting v3.2.0.

## Regression and release acceptance

`tests/DlssNrManager.Tests/RealEsrganPublishedModelRepairTests.cs` reconstructs each of the five historical published bytes from the vendored upstream source, verifies each published SHA-256, exercises the actual `VerifyAndRestoreAsync` file conversion, compares byte-exact canonical source outputs, verifies an untouched LF model, rejects a changed SHA-256 byte and unknown pins, and guards against mutable `latest` model endpoints. Existing checkout integrity and immutable receipt tests must continue passing.

**Pass criteria for code merge:** signed protected PR, exact head Windows Build/xUnit, CodeQL Analyze C#, full deterministic ZIP checks, zero unresolved review threads, same base/expected SHA, exact source/merge tree parity and no V4.5. Code merge is not proof that owner PC can access GitHub; on-device fresh-download, damaged-download, cancel, retry and Vulkan RTX image/video fidelity and GPU/VRAM #95 acceptance remain separate.

**Issue disposition:** #411 may be marked *code remediation complete* after protected merge and regression tests, but must remain OPEN for actual remote file download and on-device verification unless supported independent end-to-end evidence is attached. #79 OpenMP license/provenance, #111 optional versioned Update completeness, #95 RTX lifecycle and #408 .NET support remain independent blockers.

**Release policy:** keep `v3.2.0` immutable, never silently overwrite its assets, never tag/publish V4 without operator authorization and all other release gates, and never install external-source nonapproved binaries.
