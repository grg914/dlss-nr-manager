# Backup and Rollback Policy

## Purpose

DLSS NR Manager modifies game directories, application binaries, runtime caches and configuration files.

Changes that can break a game/application must have a defined rollback strategy before production promotion.

## Game installation/update

Managed game modifications should use:

- a manifest of files owned by DLSS NR Manager;
- a backup before replacing colliding/existing files;
- an operation journal for multi-step writes;
- managed-file hashes where practical;
- rollback on failed install/update;
- interrupted-transaction recovery.

Do not delete unrelated files merely because they look similar to a managed component.

## Existing user files

When a managed install collides with an existing user/game file:

- preserve the existing file in the managed backup when possible;
- record enough information to restore it;
- only delete the replacement on rollback if the manager introduced it.

## Configuration changes

Changes to OptiScaler, ReShade, proxy DLL selection or other game configuration should remain within the managed install model.

Manual settings changes should not silently invalidate backup ownership information.

## Application self-update

Application update should:

1. download and verify the new release;
2. stage it separately;
3. verify expected application version;
4. retain a backup of the current executable;
5. replace only after the running process can safely exit;
6. restore the backup if the replacement fails.

## Minecraft

Minecraft setup must avoid overwriting unrelated instance data.

Manager-owned runtime/resource/mod changes should be identifiable and removable without deleting worlds, screenshots, saves or unrelated user mods unless the user explicitly requests a broader reset.

## AI / media

Installing/removing a model or runtime may delete the manager-owned local copy.

Removing a runtime/model must not delete generated outputs or source media unless that is a separately confirmed operation.

Permanent media enhancement should write a new output by default rather than overwrite the original input.

## PC Cleanup

PC cache cleanup is an explicit exception: deleted cache/temp files do not have a project-level rollback.

Because of that:

- analyze first;
- show estimated size;
- require explicit confirmation;
- restrict cleanup to reviewed cache/temp roots.

See `docs/PC_MAINTENANCE_SAFETY.md`.

## Full application-data reset

Deleting all DLSS NR Manager local data must:

- be explicitly confirmed;
- show the application data root;
- not delete unrelated user data;
- clearly state that locally downloaded components/settings/caches will be removed.

## Testing

Any new destructive/mutating feature should include at least one of:

- rollback test;
- interrupted-operation recovery test;
- safe-path/root validation test;
- explicit documented reason why rollback is not applicable.
