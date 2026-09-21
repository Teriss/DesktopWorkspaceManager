# Third-party notices

- **VirtualDesktopAccessor**, release `2024-12-16-windows11`, Jari Pennanen, MIT. The exact license and pinned binary provenance are under `third_party/VirtualDesktopAccessor` (published as `licenses/VirtualDesktopAccessor`). Source: https://github.com/Ciantic/VirtualDesktopAccessor
- **.NET 10 / System.Drawing.Common**, Microsoft and contributors. Distributed under their respective .NET package licenses; runtime license notices are included in the self-contained output. Source: https://github.com/dotnet/runtime
- **Windows App SDK 2.5.1 / WinUI 3**, Microsoft. Used and redistributed under the licenses supplied in the NuGet packages. Source and release details: https://github.com/microsoft/WindowsAppSDK
- **xUnit / Microsoft.NET.Test.Sdk**, development/test dependencies only. Licenses are in the corresponding restored NuGet packages; these assemblies are not included in the application release.

Windows App SDK and .NET transitive package versions are recorded in each project's `packages.lock.json`. The publication script creates SHA256SUMS.txt for the shipped files.

The maximized-window placement sequence follows the behavior documented by the PowerToys Workspaces WindowArranger: position on the destination monitor in restored state, then maximize. Reference: https://github.com/microsoft/PowerToys/blob/main/src/modules/Workspaces/WorkspacesWindowArranger/WindowArranger.cpp
