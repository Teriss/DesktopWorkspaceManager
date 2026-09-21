# Desktop Workspace Manager

Desktop Workspace Manager is a native Windows 11 utility for managing virtual desktops, physical monitors, and top-level application windows from one visual surface. It is designed for people who keep development, communication, games, and downloads open at the same time and need to move a window without first navigating through Win+Tab.

The current release is **0.1.4**, a focused first usable version. It concentrates on reliable discovery, live previews, and combined desktop/monitor moves. See the [0.1.4 release notes](docs/RELEASE_NOTES-0.1.4.md) for the crash fix and validation scope. Workspace definitions, application launch plans, and PowerToys import are planned for a later release.

## Highlights

- Windows 11 Fluent UI with Mica, rounded cards, theme-aware colors, and high-DPI layout.
- A desktop strip that distinguishes the desktop being viewed from the desktop currently in use.
- Monitor panels that group windows by physical display and automatically adapt their card density.
- Real DWM live thumbnails for ordinary top-level windows, including a best-effort cached frame for minimized windows.
- Drag a card to a desktop to change its virtual-desktop membership, or to any part of a monitor panel to move it to that monitor too. Hover a desktop card for 400 ms to expose its monitor targets.
- Right-click menus provide the same move operations when dragging is inconvenient.
- The hover close button sends `WM_CLOSE`; it does not terminate a process, so an application can save, cancel, or redirect the close request.
- Errors remain visible until dismissed. Routine success, information, and warning states do not create a notification banner.
- Mixed-DPI, negative-coordinate, portrait-monitor, taskbar, maximized, and minimized-window handling.
- Single-instance operation with a tray icon and configurable global hotkey (`Win + \`` by default).
- Ordinary user permissions; no automatic elevation and no window-image logging.

## Requirements

The validated target is:

- Windows 11 25H2 x64
- OS build `26200.6899`
- .NET SDK `10.0.300` (or a later servicing build in the same feature band)
- Windows SDK `10.0.26100`
- Visual Studio Build Tools / MSBuild with desktop C# workloads

The virtual-desktop adapter is enabled only for the validated Windows build. On an unknown build, the application keeps its monitor and window features available and presents a compatibility error instead of calling an unverified private interface.

## Download and run

The portable release contains the .NET runtime, Windows App SDK, compiled XAML resources, native adapter, licenses, and checksums. Extract the complete directory and run:

```powershell
.\WorkspaceManager.App.exe
```

Do not copy only the executable. The whole `win-x64` directory is the distribution unit.

The repository also contains the locally generated release directory at:

```text
artifacts/publish/win-x64/WorkspaceManager.App.exe
```

The manager opens on the monitor containing the pointer. Closing the panel or pressing `Esc` hides it while the tray process remains available. Starting the executable again activates the existing instance.

## Interaction model

The top desktop cards select what is shown. The emphasized card is the desktop being viewed; a `当前所在` caption identifies the system's current desktop. `进入桌面` switches the system desktop and closes the panel.

Window cards support the following operations:

1. Click a card to switch to its desktop, activate the window, and hide the manager.
2. Hover the title row to reveal the close button in the upper-right corner.
3. Drag to a desktop card to change only desktop membership.
4. Drag to a monitor panel, including its blank space, to change desktop membership and monitor placement together.
5. Right-click for `打开窗口` and `移动到` commands.
6. Press `Esc` to cancel a drag or close the panel.

The manager keeps the panel open after a move. Window position is calculated in the target monitor's work area, preserving logical size across DPI changes and clamping oversized or partly off-screen rectangles. Maximized and minimized state is restored after a move where Windows permits it.

The close button posts `WM_CLOSE` after revalidating the HWND, process ID, and process creation time. A save dialog or close-to-tray policy therefore remains under the application's control. If the source disappears, its card and native preview host are released during the next refresh without invalidating unrelated previews.

## Build

Restore, compile, and run the complete build from PowerShell:

```powershell
.\scripts\Build.ps1
.\scripts\Run.ps1
```

The solution is split into four runtime layers and one test project:

```text
src/WorkspaceManager.Core              Models, contracts, placement, move coordination
src/WorkspaceManager.Windows           Win32, DWM, monitors, virtual-desktop adapter, tray
src/WorkspaceManager.App              WinUI 3 presentation and interaction layer
tests/WorkspaceManager.Tests          Deterministic unit tests
tools/WorkspaceManager.Diagnostics    Isolated Windows integration fixtures
```

The UI depends on service interfaces rather than calling Win32 directly. Window identities always include HWND, PID, and process creation time to guard against handle reuse. Desktop calls are serialized and the adapter is rebuilt when Explorer restarts.

## Verification

Run the deterministic tests:

```powershell
.\scripts\Test.ps1
```

Optional checks use only dedicated test windows and temporary desktops:

```powershell
.\scripts\Test.ps1 -Integration       # desktop and monitor movement
.\scripts\Test.ps1 -Preview           # DWM registration, cropping, wheel forwarding
.\scripts\Test.ps1 -Close             # WM_CLOSE semantics and stale identities
.\scripts\Test.ps1 -UiSmoke           # hidden WinUI/resource lifecycle check
.\scripts\Test.ps1 -UiInteraction     # visible drag/menu/dialog regression check
.\scripts\Test.ps1 -UiClose           # real manager close-button lifecycle
```

`-UiClose` starts seven uniquely tagged fixture windows in a separate diagnostics process. It checks normal close, an application that cancels the first close request, external source disappearance, card removal, preview-host disposal, retention of unrelated card instances, and repeated layout passes after a DWM source becomes invalid. It never selects or closes an ordinary user application.

Create a self-contained release and ZIP:

```powershell
.\scripts\Publish.ps1 -Zip
```

The publish script runs the actual published executable's smoke test before creating the archive, copies documentation and third-party notices, and writes `SHA256SUMS.txt`.

## Repository layout and local data

User settings and rotating operation logs are stored under:

```text
%LOCALAPPDATA%/DesktopWorkspaceManager/
```

Logs contain operation types, results, exception types, HRESULTs, and stack traces. They do not contain window titles, command lines, or preview pixels. The application does not upload telemetry or window content.

## Scope and known limitations

- Private virtual-desktop calls are guarded to the validated Windows build and can change with Windows updates.
- A window fixed to all desktops can be moved across monitors, but the first release does not change its fixed state through drag-and-drop.
- DWM cannot provide a useful frame for every protected, permission-restricted, never-painted, or minimized window. Those cards fall back to an icon, title, and state.
- Windows may reject activation, closing, or geometry changes across integrity levels. The manager reports the error and does not elevate itself.
- Workspace persistence and launch plans, PowerToys import, independent virtual desktops per monitor, Shell injection, resource monitoring, Win+Tab replacement, installation, and auto-update are outside this release.

## Third-party components

Virtual desktop access is provided by the pinned x64 build of [VirtualDesktopAccessor](https://github.com/Ciantic/VirtualDesktopAccessor). Its binary, checksum, README, and license are included in `third_party/VirtualDesktopAccessor` and the published `licenses` directory. Other package notices are collected during publishing.

## License

The project is released under the [MIT License](LICENSE). Third-party components retain their own licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and the notices included in the portable release.
