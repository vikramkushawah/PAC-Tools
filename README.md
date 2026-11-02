# PAC Tools — Revit add-in

A Revit add-in for **Tandon Consultants Pvt. Ltd. (TCPL)** that connects **Excel spreadsheets to Revit
schedules**: import a worksheet as a schedule, keep track of the source workbook, and refresh the schedule
when the spreadsheet changes.

This repository is **separate from TCPL BIM Tools** and carries **no licensing code** — it is set up to be
worked on by an outside developer.

> **Status: scaffold + approved design, implementation not started.**
> The add-in builds, installs and runs today, with one sample tool that proves the whole pipeline.
> The Excel work is specified in **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** and has not been built yet.
> **Read that document before writing any code** — it establishes the one Revit API constraint that
> decides the entire design.

---

## What it will do

1. **Import spreadsheet data** — pick a workbook and worksheet, map the columns, get a Revit schedule.
2. **Refresh linked data** — the link to the source workbook is remembered; refresh when the sheet changes.
3. **Schedules from a selection** — a named table, a named range or a typed A1 range becomes its own schedule.

The short version of *how*: a Revit schedule is a query over Revit's own database, and
`SetCellText` is forbidden in a standard schedule's body — so spreadsheet values have to live in
**parameters on Revit elements** before a schedule can show them. The design uses one geometry-free
carrier element per spreadsheet row, a fixed pool of shared parameters renamed per schedule, and
Extensible Storage for the link record. The reasoning, the alternatives and the costs are in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Supported versions

| | |
|---|---|
| Revit | **2025 and 2026** (both .NET 8). Revit 2024 and older are not targeted. |
| Excel | `.xlsx`, `.xlsm`, `.xls` (legacy, read-only), `.csv`. **Excel does not need to be installed** — no COM Interop. |
| Build | .NET 8 SDK, Visual Studio 2022 17.8+ or `dotnet` CLI. Revit must be installed to build (the API DLLs are referenced from it). |

---

## Install (the easy way)

**Close Revit first** — it keeps the add-in DLL locked while it is running. Then double-click:

```
install\Install-PACTools.bat
```

It checks that Revit is closed and that the .NET SDK and the Revit API are present, builds the add-in,
copies it in, and hash-verifies what it installed against what it built. No administrator rights needed —
nothing is written outside your own user profile. Every failure tells you what to do about it.

Start Revit, and a **PAC Tools** tab appears on the ribbon next to TCPL BIM Tools, with two panels:
*Model* (Element Count) and *Help* (About). Revit may show a one-time "unknown add-in" prompt — choose
**Always Load**.

To remove it: `install\Uninstall-PACTools.bat`. That removes the add-in, not any project data.

Options, run from a terminal rather than double-clicked:

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File install\install.ps1 -RevitVersion 2026
```

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File install\install.ps1 -RevitInstallDir "D:\Autodesk\Revit 2025"
```

`-Pull` updates the repository first, `-SkipBuild` installs what is already built, `-Configuration Debug`
installs a debug build, `-Uninstall` removes it.

## Build and install by hand

```bash
dotnet build src/PacTools/PacTools.csproj -c Release
```

```bash
dotnet build src/PacTools/PacTools.csproj -c Release -p:DeployToRevit=true
```

The second command installs for the current Windows user:

```
%AppData%\Autodesk\Revit\Addins\2025\PacTools.addin      the manifest Revit reads
%AppData%\Autodesk\Revit\Addins\2025\PacTools\           the add-in itself
```

Restart Revit; a **PAC Tools** tab appears on the ribbon.

To remove it again:

```bash
dotnet build src/PacTools/PacTools.csproj -t:UninstallFromRevit
```

Revit installed somewhere else, or a different version:

```bash
dotnet build src/PacTools/PacTools.csproj -c Release -p:RevitVersion=2026 -p:RevitInstallDir="D:\Autodesk\Revit 2026\"
```

## Checks (no Revit needed)

```bash
dotnet build tests/PacTools.Checks/PacTools.Checks.csproj -c Release -o tests/PacTools.Checks/bin_out
```

```bash
tests/PacTools.Checks/bin_out/PacTools.Checks.exe
```

It prints one `PASS` / `FAIL` line per check and exits non-zero if any failed. The view models run against
a fake Revit, so these run on any machine — use them in CI and before every push.

## Layout

```
PacTools.addin              the Revit manifest. NEVER change AddInId once a build has been shared
PacTools.sln
src/PacTools/
  Core/                     no Revit API, no Excel library - the logic, and the interfaces
    Brand.cs                names, colours, the ribbon tab, in one place
    RevitRunner.cs          IRevitRunner, the exception types, modeless state
    ModelService.cs         what the sample tool needs from the model
    CountReport.cs          the sample tool's logic (pure C#)
  Revit/                    the ONLY place the Revit API is used
    App.cs                  builds the ribbon - add a tool by adding one PushButtonData
    Modeless.cs             RevitQueue + ModelessWindows: why windows never freeze Revit
    Icons.cs                ribbon icons, drawn in code
    ElementCountCommand.cs  the worked example - copy this to make a new tool
    AboutCommand.cs         the simplest kind of tool: no window
    RevitModelService.cs    reads the model
  UI/                       WPF, MVVM, no Revit API
    Mvvm.cs                 ObservableObject, RelayCommand, Choice<T>
    ElementCountWindow.xaml + .cs, ElementCountViewModel.cs
tests/PacTools.Checks/      off-screen checks: run the logic and the view models with a fake Revit
install/
  Install-PACTools.bat      double-click to build and install for the current user
  Uninstall-PACTools.bat    double-click to remove it
  install.ps1               the real work: checks, build, copy, hash-verify
docs/ARCHITECTURE.md        the Excel-to-schedule design. Read this first
CONTRIBUTING.md             how to work in this repository
CLAUDE.md                   project memory, for picking the work up cold
```

## The sample tool

**PAC Tools › Model › Element Count** counts the elements in the active view or the whole model, grouped by
category, and copies or saves the table as CSV. It only ever reads the model.

It exists to prove the whole pipeline works — ribbon → command → modeless window → `RevitQueue` →
service → `Core` logic → bound UI — and as the file to copy when adding a real tool. Once the Excel tools
are in, it can be deleted; nothing else depends on it.

## The two rules that matter most

1. **Revit must never freeze.** Windows are shown with `ModelessWindows.Show`, never `ShowDialog`, and
   every Revit API call from a window goes through the `RevitQueue`.
2. **`Core/` never references the Revit API or the Excel library.** That is what lets the logic be checked
   in seconds without Revit, and it is not negotiable.

Both are explained, with the reasoning, in [CONTRIBUTING.md](CONTRIBUTING.md).
