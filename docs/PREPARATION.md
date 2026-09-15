# Source preparation report

## Selection and search scope

All mounted filesystem drive roots C: through N: were searched for GHQuickSearch project/solution names and related files, including hidden folders and ignored files. Protected or unreadable paths could not be inspected. Compressed archives were not exhaustively unpacked; the matching versioned source directories were available separately.

The search found the development project, its console test project, versioned delivery directories/ZIPs, generated build outputs, and an installed `GHQuickSearch.gha` under the user's Grasshopper Libraries directory. No other independently named GHQuickSearch source project was found outside this collection. The original project did not include a GHQuickSearch solution file.

The selected development project is **0.4.16**. All seven production C# files match the corresponding files in the 0.4.16 delivery source by SHA-256. Earlier versions were not merged into this snapshot.

## Included files

- `src/GHQuickSearch/GHQuickSearch.csproj` and the seven original C# files: `AssemblyInfo.cs`, `CompactPopup.cs`, `ComponentCatalog.cs`, `FavoritesStore.cs`, `PluginRuntime.cs`, `RadialPopup.cs`, `SearchPopup.cs`.
- `src/GHQuickSearch.Tests/GHQuickSearch.Tests.csproj` and `Program.cs`, preserving the existing relative project reference.
- A new root `GHQuickSearch.sln` containing both projects.
- A new root README describing the current implementation, build and installation requirements, usage and credits.
- A root `.gitignore` for Visual Studio, C#, Grasshopper/Rhino binaries and local test/user data.
- This preparation report.

The production C# files and project file are byte-identical to the originals. No branding edits were made to assembly metadata or implementation code.

`RadialPopup.cs` is retained as original compiled source. The legacy `SearchPopup.cs` includes a layout helper used by `CompactPopup.cs` and is referenced by retained code and tests. Removing these files would require a separate source refactoring, which was outside this preparation.

## Excluded files

- `bin`, `obj`, `.vs`, Debug/Release output and Visual Studio user/cache files.
- Compiled `.gha`, `.dll`, `.exe`, `.pdb` files and previous delivery ZIPs.
- Existing test logs, generated preview images, temporary files and personal favorites/settings.
- Superseded README/API notes describing earlier radial menus and settings windows. Their current build/dependency information is incorporated into the new README.
- Decompiled vendor research files and local investigation tools.
- `RunInRhino.py`, an optional historical QA script that switches the active document and is not required to build or run the console harness. Its relative assumptions and document-changing behavior were not brought into this source snapshot.

There were no required external resource files or Properties/Resources directories to copy. Empty placeholder resource directories were not created.

## Dependency decisions

RhinoCommon, Grasshopper and GH_IO are required references supplied by the Rhino 8 installation. They are documented, not redistributed. Windows native DLLs and .NET Framework assemblies are also platform dependencies, not repository files. Component icons are obtained at runtime from Grasshopper; custom UI graphics are drawn in C#.

No explicit third-party runtime NuGet dependency appears in the project file. The local verification used installed .NET Framework targeting support; other machines may need the targeting pack or reference-assembly restore.

## Verification

The newly prepared root solution was built using `dotnet build GHQuickSearch.sln -c Release` on Windows with SDK **10.0.401** and the installed Rhino/Grasshopper assemblies reporting file version **8.35.26251.13001**.

- Build: **succeeded, 0 warnings, 0 errors**.
- Existing console harness: **exit code 0; 36 checks passed**.
- Production source/project hash comparison: **all matched the original files**.
- Optional in-process Rhino integration and interactive preview modes were not run.

Build and test artifacts were generated only inside this new folder for verification. Automated approval rejected their cleanup with `blocked by policy`, including a retry naming only the verified generated directories. These ignored directories therefore remain on disk and must be removed manually for a source-only physical folder:

```text
src/GHQuickSearch/bin/
src/GHQuickSearch/obj/
src/GHQuickSearch.Tests/bin/
src/GHQuickSearch.Tests/obj/
.verification/
```

Do not remove the original development project. All five paths above are relative to this prepared folder. `.gitignore` excludes these generated files from source control; do not force-add them.

Original development files, installed plug-ins and previous delivery folders were not edited, moved or deleted. This preparation creates a local folder only; nothing has been pushed to GitHub.
