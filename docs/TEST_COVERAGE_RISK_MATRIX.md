# Test Coverage and Risk Matrix

This file records security/reliability areas that should have deterministic regression coverage. It is not a replacement for manual end-to-end validation.

| Area | Existing automated coverage | Risk | Next regression coverage |
| --- | --- | --- | --- |
| Game transaction journal/path containment | Yes | High | Keep/expand interruption and rollback cases |
| Renderer/game detection | Yes | Medium | Keep engine/wrapper variants current |
| Managed-file integrity | Yes | High | Add multi-file partial corruption cases |
| NVIDIA runtime pair policy | Yes | High | Add signer/package provenance cases where testable |
| Minecraft native Vulkan/NGX policy | Yes | High | Keep forbidden proxy/runtime matrix current |
| UI FR/EN normalization | Basic | Medium | Add feature-specific static/dynamic string audits |
| Network retry behavior | Yes | Medium | Add cancellation/timeout boundaries |
| SafeZip traversal/expansion | Implementation present; direct tests limited | High | Add traversal, duplicate path, expansion and unexpected executable tests |
| Application self-update | Implementation hardened; direct tests limited | High | Add ZIP identity/hash/rollback tests |
| PC Cleanup allow-list | Limited/no dedicated tests | High | Add allow-list and junction/reparse-point tests without touching real user caches |
| Diagnostics/support bundle sanitization | Limited/no dedicated tests | Medium | Add profile-path redaction and bundle-content tests |
| Media Neural install/remove | Feature tests limited | High | Add install-state, missing dependency and cancellation tests |
| Real-ESRGAN package/model integrity | Runtime validation present | High | Add model mismatch and clean redownload tests |
| AI-origin detector package integrity | Runtime validation present | High | Add removal/reinstall and hash-failure tests |
| Central Downloads | Added in active feature work | High | Keep >1 GiB consent, install/remove/redownload and restricted-license tests |
| AI Studio license gating | Added in active feature work | High | Add license identity change/reacceptance and import containment tests |
| AI Studio chunk reconstruction | New/active work | High | Add missing chunk, wrong size/hash, ordering and cancellation tests |
| VSR-HDR/VLC arguments | Added in active feature work | Medium/High | Keep VSR/HDR/fullscreen/OSD argument matrix |
| Restore HD Video | Active work | High | Add x1/x2/x4 pipeline-selection and output safety tests |
| Release asset notices/source obligations | CI policy newly added | High | Verify required notice/source assets in release workflow |
| Runtime seed refresh | CI workflows | High | Keep concurrency/stale-run/release-only tests or workflow assertions |

## Release gate

A high-risk feature should not be treated as production-complete solely because it compiles.

Before release, high-risk rows changed by the release should have either:

- deterministic automated regression coverage; or
- a documented manual validation result/checkpoint when deterministic automation is impractical.

## Priorities

Highest-priority remaining automated tests:

1. SafeZip malicious archive cases.
2. App-update archive/hash/rollback behavior.
3. PC Cleanup allow-list/reparse-point containment.
4. AI Studio chunk reconstruction and imported-model containment.
5. Release-package notice/source completeness.

