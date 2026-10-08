# PC Maintenance Safety Policy

## Scope

This policy applies to **PC Update Center** and **PC Cleanup**.

These features operate outside the DLSS NR Manager application directory and therefore require stricter user-consent and path-safety rules.

## PC Update Center

### Scanning

Read-only scans may inspect:

- WinGet upgrade inventory;
- Chocolatey outdated inventory when available;
- Windows Update Agent;
- installed driver/firmware metadata;
- registry metadata needed to identify installed software/drivers.

A scan must not silently install updates.

### Update actions

Bulk WinGet updates require an explicit confirmation that explains:

- `winget upgrade --all` will run;
- third-party installers may open;
- administrator rights may be requested;
- applications may restart or change independently of DLSS NR Manager.

Vendor/support links opened from cached scan data must be restricted to trusted Windows Settings URIs or HTTPS URLs.

### External package managers

WinGet, Chocolatey and Windows Update are external system package/update mechanisms.

Their packages are not "manager-owned" DLSS NR Manager artifacts and must not be represented as if this project verified every third-party installer hash.

## PC Cleanup

### Allowed targets

Cleanup must remain limited to explicit, reviewed cache/temp roots such as:

- current-user temporary files;
- Windows temporary files;
- DirectX shader cache;
- NVIDIA shader caches;
- other cache roots added through code review.

Do not add arbitrary user-selected deletion roots to the default cleanup flow.

### Analyze before delete

The application should analyze selected roots and show estimated reclaimable size/file count before deletion.

Cleanup requires explicit user confirmation.

### Path safety

The cleanup implementation must:

- normalize/canonicalize paths;
- reject paths outside the intended reviewed roots;
- avoid following a path escape into unrelated user/system data;
- skip locked/in-use files rather than forcing deletion;
- delete only selected categories.

### Rollback

Cache cleanup is inherently destructive and does not have a file-level rollback.

The confirmation must therefore explain that caches may be rebuilt by Windows, GPU drivers, games or applications.

Do not extend PC Cleanup to documents, downloads, save games, project folders or arbitrary application data without a separate design and recovery plan.

## Application-data deletion

"Delete all DLSS NR Manager local data" is separate from PC Cleanup.

It must:

- show the exact application-data root;
- require explicit confirmation;
- run after the app closes when needed;
- never expand into unrelated user folders.

## Logging/privacy

Maintenance logs must not capture passwords, access tokens or file contents.

Paths may be sensitive; diagnostic exports should be user-reviewable before sharing.
