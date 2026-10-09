# V4.5 native WPF visual integration — GPT C

Status: **isolated experimental UI implementation; not released and not merged to stable main**.

## Scope

- Imports the V4.5 color, brush and style dictionary into the real WPF resource tree.
- Remaps existing global palette keys to navy/blue/green without replacing controls or commands.
- Applies the sidebar/title-bar treatment and a 236 px navigation rail, leaving menu ordering intact.
- Adds four native AI Studio quick-mode buttons wired to the **existing** task-selection pipeline. No separate inference or model execution is introduced.
- Preserves the original `assets/branding/logo.png` and `assets/branding/app.ico` paths.

## Verification and limits

- Source checked: all prior `x:Name` values retained; all four buttons call one handler; no new NuGet dependencies, downloads, WebView or Python execution.
- A synthetic xUnit markup test checks the quick buttons, task coverage and brand paths.
- **Windows WPF compilation/xUnit/CodeQL and physical FR/EN/DPI/keyboard/screenshot review: pending exact-HEAD CI/device verification.** No claim of successful generation.
- No runtime promotion; video remains subject to existing eligibility gates. Issue #285 is still blocking AI runtime execution.
- Full dashboard illustrations, image gallery, Online opt-in control, all individual responsive mode layouts are **not yet integrated**. The SVG references remain an owner-maintained design kit.

## Rollback

Revert the isolated feature commit or close the draft PR. No model files, settings schema, runtime data, downloads, protected main code or release assets are touched.
