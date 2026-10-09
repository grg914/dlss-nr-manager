# R069 — Optional AI Studio manager-owned model updates in Download Center

Issue #111 partial scope: after a manager-owned AI Studio model is successfully reconstructed from a verified chunk/archive manifest, its stable version, package id, tag and SHA-256 are written **inside the staged model directory**. Its transactional replacement moves this receipt together with the model; rollback restores the previous receipt. Manually imported and restricted-license models never receive such a receipt.

The existing Download Center **Refresh** action now checks the selected, installed, redistributable model against manager-owned published package metadata, without downloading/installing anything. The existing **Redownload** button changes to **Update / Mettre à jour** only when a newer strictly parsed stable version and different verified archive SHA-256 are both proven. It still invokes the existing staged/rollback model installer; there is no duplicate update button. Unknown local version, offline/invalid/unrelated manifest, a restricted-license model, and an older/same release never offer an update. Tests cover those cases.

The previous media-engine update logic remains unchanged. Other runtimes (VLC, Real-ESRGAN, detector) have no verified local release version receipts and **must not be called outdated** by comparing opaque asset names; issue #111 remains OPEN pending similar provenance and safe reinstallation plus physical Windows tests. OpenMP #79 and runtime-seed #123 are separate safety PRs.

This is a new feature proposal. Do not merge until fresh Build/CodeQL and xUnit regression tests are green on exact head.
