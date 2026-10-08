# Data, Privacy and Local Storage Policy

## Project policy

DLSS NR Manager should operate locally by default and must not add analytics, advertising telemetry, behavioral tracking, or automatic upload of user media without an explicit product decision and user consent.

This document defines the required behavior for new code and features.

## User media

Images, videos, Minecraft files, AI prompts, AI outputs and local game files are user data.

Rules:

- process locally when the selected feature is local;
- never upload user media merely to inspect, upscale, restore, detect or generate content unless the user explicitly selected a network-backed feature;
- do not include user media in diagnostics, issue templates or crash reports automatically;
- temporary copies must be cleaned up when no longer required where practical.

## Network access

Expected network operations include explicit component/update workflows such as:

- checking this repository's GitHub Releases;
- downloading manager-owned runtime/model packages;
- downloading/installing user-approved components;
- opening an official license/model page at the user's request;
- development/upstream-refresh workflows.

Production feature code must not introduce unrelated analytics endpoints.

## Local state

Application-managed data may include:

- component caches;
- runtime packages;
- AI models;
- AI Studio job manifests;
- generated outputs;
- license-acceptance records;
- update metadata;
- logs and diagnostics.

Feature owners must document where persistent data is stored and provide removal/reset behavior for large managed components when reasonable.

## License acceptance records

Local AI model license acceptance records may contain:

- model id;
- license name;
- official license URL;
- acceptance timestamp.

They should not contain account credentials or access tokens.

Acceptance records are local application state and are not a substitute for upstream access control or licensing.

## Tokens and credentials

Tokens for GitHub, Hugging Face, NVIDIA or other providers must not be written into source control, logs or ordinary app configuration in plaintext unless explicitly designed as a secure credential store.

## Logs

Logs should avoid:

- access tokens;
- passwords;
- complete authorization headers;
- personal file contents;
- raw media payloads.

File paths may still be sensitive. Diagnostic exports should be reviewable by the user before sharing.

## Telemetry changes

Any future telemetry/crash-upload feature must be:

- documented;
- opt-in by default unless a separate explicit product/privacy decision is made;
- limited to necessary fields;
- separable from local/offline operation;
- removable/disableable by the user.

## Offline mode

Manager-owned components that have already been installed should remain usable offline where their underlying technology permits it.

An offline operation must not silently turn into an external upload fallback.
