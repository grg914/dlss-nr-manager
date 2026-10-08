# Download and Runtime Policy

## Single installation surface

Components managed by DLSS NR Manager should have one authoritative install/remove/redownload surface. Feature pages may show status and navigate to that surface, but must not silently trigger duplicate downloads.

## Manager-owned first

For redistributable components, production prefers assets published from `grg914/dlss-nr-manager` releases. Upstream repositories are inputs to controlled vendoring/bootstrap workflows, not normal runtime dependencies.

## Integrity

Before activation, downloaded content must be validated with one or more of:

- GitHub release SHA-256 digest;
- pinned SHA-256/SHA-512/SHA-1 fingerprint where appropriate;
- signed component manifest;
- vendor Authenticode signature;
- immutable source commit/tree provenance.

Integrity failure is terminal: do not retry a known-bad hash/signature as if it were a transient network error.

## Large downloads

A user-visible confirmation is required before downloads whose complete package size exceeds 1 GiB.

GitHub's per-asset limit is a transport constraint only. Large AI packages may be split into multiple release assets and reconstructed locally. DLSS NR Manager must verify each chunk and the reconstructed archive/package before installation.

## Archive extraction

Downloaded archives must:

- reject absolute/traversal paths;
- enforce entry-count and expansion bounds appropriate to the component;
- extract to staging first;
- replace the target atomically where practical;
- preserve or restore the prior install if activation fails.

## Offline behavior

Once manager-owned runtimes/models are installed, supported local workflows should not require the original upstream repository. Offline-capable components should load only from the managed local cache/runtime directories.

## Restricted/gated content

License acceptance by an end user does not automatically grant this project redistribution rights. Gated/restricted model files may require the user to obtain files from the official source and import them locally.

## Removal and redownload

Removing a managed component must delete only manager-owned paths. Redownload/reinstall must not overwrite unrelated user files.
