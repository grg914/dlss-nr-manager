# V4 Windows test without an installed Manager — local portable build

**Status:** application-only development build, not a production release or installer. No software installation or GPU/rollback acceptance is claimed by this document. Source from protected signed \`main\` only, not an experimental V4.5 branch.

## Before starting

On Windows 11 x64, use a **dedicated test account** when possible because the Manager may create files beneath \`%LOCALAPPDATA%\DlssNrManager\` even when its executable is unpacked without an installer. Back up any existing data first. A self-contained executable means .NET runtime is bundled; it does **not** mean the application leaves no local state.

Build prerequisites (installed manually if missing):
- Git for Windows, preferably with Git LFS, from https://git-scm.com/download/win
- **.NET 8 SDK** from https://dotnet.microsoft.com/en-us/download/dotnet/8.0

Do not install runtime dependencies automatically or obtain SDKs through unofficial websites. The helper checks both prerequisites and stops on missing SDK.

Open a **normal, non-elevated Windows PowerShell**, then use a separate clone. No user files or existing local project working trees are modified:

\`\`\`powershell
$source = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'DLSSNR-V4-Portable-Source'
if (Test-Path -LiteralPath $source) { throw 'The dedicated source folder already exists; choose another clean clone folder.' }
$env:GIT_LFS_SKIP_SMUDGE = '1'
git clone --depth 1 --branch main https://github.com/grg914/dlss-nr-manager.git $source
if ($LASTEXITCODE -ne 0) { throw 'Git clone failed.' }
Set-Location $source
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build-local-v4-portable-test.ps1
\`\`\`

The helper performs: (1) clean protected-main source/SDK checks; (2) downloads **only** the pinned SHA-256 checked offline .NET package seed from the repository's existing manager-owned \`runtime-seed-v1\` cache; (3) restores app and tests using a local NuGet.Config with no public package feeds; (4) runs xUnit; (5) publishes a self-contained x64 single-file WPF EXE; (6) generates the repository's deterministic application-only ZIP, checks the extracted EXE SHA-256 and writes a source/hash receipt. It does not start the app or delete/rewrite an existing test folder.

Default result:

\`\`\`text
Desktop\DLSSNR-V4-Test-<12-character-main-SHA>\
  DlssNrManager.exe
  README.md
  LICENSE
  THIRD_PARTY_NOTICES.md
  DATA_PRIVACY.md
  BUILD-RECEIPT.txt
\`\`\`

**Important:** The local EXE may be **Authenticode-unsigned** even though GitHub source commits are signed. Never treat a GitHub commit signature as a Windows code-signing certificate or disable antivirus/SmartScreen globally. Inspect \`BUILD-RECEIPT.txt\` and the code provenance before launching.

## Testing after the build

Launch the EXE manually from the dedicated folder, only after verifying the local receipt. To collect a non-destructive Windows/GPU preflight, open Windows PowerShell from the same source checkout and run:

\`\`\`powershell
$exe = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'DLSSNR-V4-Test-<12-character-main-SHA>\DlssNrManager.exe'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\prepare-v4-rtx-acceptance.ps1 -ManagerExecutablePath $exe
\`\`\`

Replace the placeholder with the exact folder printed by the builder. The preflight writes \`%TEMP%\dlssnr-v4-rtx-acceptance\v4-rtx-preflight.json\` with all physical test cases explicitly set to \`NOT_RUN\`. It is not an end-to-end test; follow \`docs/V4_RELEASE_GATES_411_79_95_2026-10-10.md\` for isolated runtime and rollback exercises **only after** package licensing and hashes are approved.

The five historically CRLF-transformed Real-ESRGAN \`v3.2.0\` assets remain uncorrected in that public release. This builder intentionally does **not** redistribute model weights, OpenMP DLLs or third-party runtime components; do not interpret a successful UI launch as permission to install or validate those components. Keep #411/#79/#95/#111 OPEN.

The build does not change the public stable release and is not a substitute for a signed, immutable future V4 release candidate.
