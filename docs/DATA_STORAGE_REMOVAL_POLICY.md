# Data Storage, Removal and Uninstall Policy

## Scope

DLSS NR Manager stores manager-owned runtime data, preferences, logs and caches primarily under:

`%LOCALAPPDATA%\DlssNrManager`

Feature-specific managed roots currently include:

- `preferences.json` — application preferences such as UI language;
- `logs\` — current/previous application logs;
- `media-engine\` — Media Neural runtime, FFmpeg and Real-ESRGAN;
- `ai-origin-detector\` — AI-origin ONNX models;
- `ai-studio\` — AI Studio runtime/models/workflows/jobs/outputs when that feature is installed;
- `vlc\` — manager-owned VLC runtime when VSR-HDR/VLC is installed.

Other feature caches, update staging and artwork caches may also live below the same application root.

## Managed vs user-owned data

The application must distinguish between:

### Manager-owned data

Files downloaded, generated or staged by DLSS NR Manager for its own operation.

Examples:

- runtime binaries;
- AI models installed from manager-owned packages;
- imported copies of manually licensed AI models;
- application logs/preferences;
- artwork/catalog caches;
- update staging;
- AI Studio job metadata/workflows stored under the manager root.

These may be removed by the application's own reset/remove/uninstall tooling when the action clearly targets them.

### User-owned data

Files selected as inputs or destinations outside the manager root.

Examples:

- source videos/images;
- game installations;
- manually downloaded official model folders selected for import;
- user-selected output folders;
- generated output files already written to a user-selected destination;
- exported support bundles.

These must not be deleted merely because the related manager-owned component is removed.

## Downloads menu removal

The centralized **Downloads** surface may remove the managed local copy of a component.

Removal rules:

- remove only the component's managed directory/files;
- do not delete external source folders used for import;
- do not delete user media;
- do not delete generated output saved outside the managed component directory;
- do not delete unrelated sibling components;
- preserve the ability to reinstall later.

For restricted/gated AI models, removing the imported managed copy does not imply deleting the user's original official download.

## AI Studio

AI Studio can be large.

Managed locations:

- `ai-studio\runtime`
- `ai-studio\models`
- `ai-studio\jobs`
- `ai-studio\workflows`
- `ai-studio\outputs`

The Download Manager removes individual managed model directories, not the entire AI Studio root.

A future "Reset AI Studio" action should offer separate choices for:

- runtime;
- models;
- jobs/workflows;
- generated outputs inside the managed output folder;
- locally recorded license acceptance.

It must not erase external user-selected output folders without a separate explicit destructive confirmation.

## VLC

Removing the manager-owned VLC component should remove only:

`%LOCALAPPDATA%\DlssNrManager\vlc`

It must not uninstall or modify a separately installed system VLC.

## Media Neural / Real-ESRGAN

Removing these components must stay within:

`%LOCALAPPDATA%\DlssNrManager\media-engine`

Real-ESRGAN removal should not remove unrelated media-engine files unless the user explicitly removes the shared media runtime.

## AI-origin detector

Removing the detector should stay within:

`%LOCALAPPDATA%\DlssNrManager\ai-origin-detector`

## Application uninstall / portable deletion

Deleting only `DlssNrManager.exe` does not automatically delete LocalAppData.

This is intentional to avoid silent data loss.

A complete manual cleanup may remove:

`%LOCALAPPDATA%\DlssNrManager`

only after the user has preserved any wanted logs, AI Studio jobs/workflows/outputs or other managed content.

## Cleanup feature distinction

PC Cleanup is separate from application-data removal.

PC Cleanup targets known regenerable Windows/NVIDIA caches and temporary locations. It must not treat the entire DLSS NR Manager data root as a generic cache unless an explicit app-reset function is implemented.

