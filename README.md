# GHQuickSearch

An enhanced double-click search interface for Grasshopper with favorites and faster component access.

GHQuickSearch is a Windows plug-in for **Rhino 8 + Grasshopper 1**. This source snapshot is version **0.4.16**.

## Features

- Double-click the canvas to open a compact favorites and search popup.
- Six favorite icons per row, with component names, categories and descriptions in hover tooltips. The favorites area opens centered on the mouse, subject to screen-edge limits.
- Search registered components through Grasshopper's component catalog. Up to nine results are visible at once, with scrolling for additional results. Available screen space can reduce the visible count.
- Search results expand upward while the search field stays in place. The best-ranked result is selected at the bottom, next to the search field.
- Click the star beside a result to add or remove a favorite. Active stars are gold.
- Click a favorite or search result to insert its component. Drag a favorite onto the canvas to insert it at the drop location and keep the popup open for more insertions.
- Hold **Ctrl** and drag a favorite to reorder it. Drop it outside the popup to remove it; the pointer displays a Delete indicator.
- Favorites and their order persist locally, with a limit of 256 entries and backup on replacement.
- **Enter** inserts the selected result, **Up/Down** navigate results, and **Esc** closes the popup. An outside mouse press dismisses it without consuming the press.
- **Shift + double-click** opens the original Grasshopper search. The middle mouse button retains Grasshopper's native behavior.

The plug-in adds a UI, not document components. Existing GH definitions do not acquire a dependency on GHQuickSearch.

## Requirements

- Windows with Rhino 8 and its bundled Grasshopper installed.
- A .NET SDK capable of building SDK-style C# projects targeting **.NET Framework 4.8** (the prepared solution was built with SDK 10.0.401).
- .NET Framework 4.8 reference assemblies/targeting support. MSBuild restore may obtain the reference-assembly package if the targeting pack is absent; package restore can therefore require network access.
- Optional: Visual Studio with .NET desktop development support.

The project uses Windows Forms, System.Drawing and Windows native UI APIs; this source does not target Rhino for macOS.

## Installation

1. Download `GHQuickSearch.gha` from the latest GitHub Release.
2. Open Grasshopper.
3. Go to **File → Special Folders → Components Folder**.
4. Copy `GHQuickSearch.gha` into the Components folder.
5. If Windows blocks the file, right-click it → **Properties** → **Unblock**.
6. Restart Rhino / Grasshopper.
7. Double-click the Grasshopper canvas to open GHQuickSearch.

> GHQuickSearch currently targets Windows with Rhino 8 and its bundled Grasshopper.

## Dependencies and resources

These assemblies are referenced from the Rhino installation, with `Private=false`:

| Assembly | Default location |
| --- | --- |
| `RhinoCommon.dll` | `C:/Program Files/Rhino 8/System/` |
| `Grasshopper.dll` | `C:/Program Files/Rhino 8/Plug-ins/Grasshopper/` |
| `GH_IO.dll` | `C:/Program Files/Rhino 8/Plug-ins/Grasshopper/` |

They are **not included in this repository**. `System.Runtime.Serialization` is a framework reference. `user32.dll`, `gdi32.dll` and `kernel32.dll` are supplied by Windows and must not be copied into the repository.

There are no external image files, `.resx` files, custom resource DLLs or explicit runtime NuGet package references in the project. Component icons come from installed Grasshopper object proxies. Stars, borders and the Delete cursor are drawn in code. Third-party Grasshopper plug-ins are only needed to search for or instantiate their own components, not to build GHQuickSearch.

## Build

From the repository root:

```powershell
dotnet build GHQuickSearch.sln -c Release
```

The compiled plug-in will be at:

```text
src/GHQuickSearch/bin/Release/net48/GHQuickSearch.gha
```

For a non-default Rhino installation, build the plug-in using the existing project properties:

```powershell
dotnet build src/GHQuickSearch/GHQuickSearch.csproj -c Release '-p:RhinoSystemDir=D:/Apps/Rhino 8/System' '-p:GrasshopperDir=D:/Apps/Rhino 8/Plug-ins/Grasshopper'
```

The existing test project and test assembly resolver use the default `C:/Program Files/Rhino 8` paths. Adjust those paths in the test project and `Program.cs` if running tests against a different installation. The production source and project were copied unchanged.

## Install or update

1. Build the project.
2. In Grasshopper, open **File → Special Folders → Components Folder** to locate the installation folder.
3. Close Rhino, then copy `GHQuickSearch.gha` into that folder, replacing the previous version. Keep only one installed copy.
4. Restart Rhino and open Grasshopper.

Compiled plug-ins belong in release downloads, not in source control. Local verification outputs are ignored by `.gitignore`; see [the preparation report](docs/PREPARATION.md) for the cleanup still required in the prepared Desktop folder.

## Tests

The included test project is a console test harness, not a `dotnet test` adapter:

```powershell
dotnet build GHQuickSearch.sln -c Release
& ./src/GHQuickSearch.Tests/bin/Release/net48/GHQuickSearch.Tests.exe
```

It checks storage, layout, result selection, favorites, insertion callbacks and mouse-message behavior. The `--integration` option additionally starts an in-process Rhino instance and exercises host APIs; it requires a working Rhino runtime/license. `--preview` opens its test canvas. These optional host modes are not part of the preparation verification.

Passing independent tests does not replace live Rhino testing of mouse capture, focus, screen scaling and initial-document creation.

## Local settings and limitations

- Favorites: `%APPDATA%/GHQuickSearch/favorites.json` (with `.bak` on replacement).
- Last error, when one is logged: `%APPDATA%/GHQuickSearch/last-error.txt`.
- Default favorites are Panel, Move, List Item, Tree Branch and Expression, resolved from installed components.
- The custom search handles registered components. Use Shift + double-click for the original search's special syntax, such as numeric sliders, direct Panel text and document-cluster suggestions.
- Favorite components from missing plug-ins cannot be inserted until those plug-ins are installed.

## Source layout

```text
GHQuickSearch.sln
src/
  GHQuickSearch/        # Plug-in project and all seven original C# files
  GHQuickSearch.Tests/  # Existing console test harness
docs/
  PREPARATION.md        # Source/dependency audit and build verification
```

`CompactPopup.cs` implements the current UI. `RadialPopup.cs` and the old settings UI in `SearchPopup.cs` remain in the original compiled source but are not exposed by the current popup. `SearchPopup.Clamp` is still used by the current layout; these files were retained to preserve build compatibility, not advertised as current features.

## Credits

**NeoStruct** — **Arash Ramezani Yekta**  
Intended repository: **NeoStructLab/GHQuickSearch**.

No license file was present in the source snapshot; this preparation does not assign a new license.
