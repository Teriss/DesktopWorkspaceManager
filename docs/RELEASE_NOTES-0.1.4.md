# Desktop Workspace Manager 0.1.4

This release fixes the crash and stale-card behavior that could occur after closing a window from the manager.

## Included

- Error-only notification banners. Routine success, information, and warning states remain silent.
- Stable preview fallback updates when a DWM source window disappears.
- Close-button lifecycle regression coverage with real fixture windows, including cancelled close requests.
- Card and native preview-host cleanup without rebuilding unrelated cards.
- 32 deterministic tests, DWM preview checks, visible WinUI interaction checks, and a self-contained x64 publish.
- MIT license and third-party notices.

## Validation target

Windows 11 25H2 x64, build `26200.6899`, with two 3840 × 2160 monitors at 150% scaling.
