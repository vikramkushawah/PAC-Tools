# PAC Tools — project memory

A Revit add-in that connects **Excel spreadsheets to Revit schedules**. Separate repository from
TCPL BIM Tools, **no licensing code**, because the work is being outsourced.

**Read `docs/ARCHITECTURE.md` first, then `README.md`, then `CONTRIBUTING.md`.** The architecture document
holds the one Revit API fact the whole design turns on.

## Where this came from

- 01-11-2026, Vineet Patwal: *"create a new revit tool call it PAC Tools and also create a new REPO because
  this is going to be outsourced so keep it seperate from the earlier tools no liscence for now"*.
- Same day, the purpose: an add-in that (1) imports a worksheet as a Revit schedule, (2) remembers the
  source workbook and refreshes, (3) makes a schedule from a selected range / table. With an assessment of
  Revit's schedule capabilities **before** implementation, then staged delivery.

## State

| | |
|---|---|
| Version | 0.1.0 |
| Built / run in Revit | **Yes, on 02-11-2026:** builds with 0 warnings against Revit 2025.4, and Revit loaded the DLL on restart. The checks pass. The tools themselves have not been exercised in Revit yet. |
| Installer | `install\` (02-11-2026): `Install-PACTools.bat` / `Uninstall-PACTools.bat` / `install.ps1`. Per-user, no admin rights, hash-verifies installed == built. `install.ps1` itself is **still not run**: the first install used `-p:DeployToRevit=true` because Revit was open. |
| Excel work | **Core layer written (02-11-2026), not compiled.** `Excel/` and `Revit/` implementations not started. |
| Scaffold | Ribbon, modeless windows, RevitQueue, MVVM, build + install + uninstall targets, off-screen checks, one sample tool. |

**AddInId `34CADD4D-D4A0-4C01-80F4-7E4CEFB792AE`, VendorId `PACT`.** Never change either: Revit identifies
an add-in by its AddInId, so a new one makes old installed copies load alongside the new one.

Assembly / namespace `PacTools`. Ribbon tab **"PAC Tools"**, panels "Model" and "Help".
Accent colour `#0F5E8C` (set in `Core/Brand.cs`, mirrored in each window's XAML).
Log: `%LocalAppData%\PACTools\logs\pactools.log`.

## The design, in three sentences

A Revit schedule is a saved query over Revit's database, and `TableSectionData.SetCellText` throws for
cells in a standard schedule body — so spreadsheet values cannot be painted into a schedule and **must live
in parameters on Revit elements**. The design therefore creates one geometry-free **carrier element** per
spreadsheet row, stores values in a **fixed pool of shared parameters** (`PAC_T01`…) whose display names are
overridden per schedule with `ScheduleField.ColumnHeading`, and keeps the link record (workbook path,
worksheet, range, column map, content hash, last refresh) in **Extensible Storage** on a `DataStorage`
element. The carriers are the previous state, so a refresh diffs against them and no row snapshot is stored.

## API facts established (01-11-2026, from the Autodesk docs — not from memory)

- **`TableSectionData.SetCellText` raises `InvalidOperationException` for cells in standard schedule body
  sections.** The body is read-only by design; its contents are generated from the `ScheduleDefinition` and
  parameters. Only the header section is writable. *This is the fact the whole design rests on.*
- Schedule columns can only come from `ScheduleDefinition.GetSchedulableFields()` — parameters that exist
  on the schedule's category, plus derived fields.
- Shared parameter fields do not appear in a new schedule by themselves; add them with
  `ScheduleDefinition.InsertField`.
- `ParameterBindings` are **per category and project-wide**: binding a parameter to Generic Models puts it
  on every Generic Model in the project. Hence the fixed parameter pool rather than one per column.
- `ViewSchedule.CreateKeySchedule(doc, categoryId)` makes abstract "key" elements, each row an element whose
  parameters are the columns. Revit's help says key schedules take **instance parameters only**.
- **02-11-2026, from Revit's *Modify a Schedule* help page:** *Insert Data Row* works for room, area,
  **key**, space and sheet-list schedules, and *"to edit a cell, click in it… or enter text"*. So body cells
  **are** editable for those types — the API prohibition is on *standard* schedule bodies only, and a key
  schedule is not one. Filters support **key schedule parameters**, up to 8 filters (one is needed).
- **Why key schedules still are not the primary choice:** key rows appear as selectable key values in the
  instance properties of **every element of that category** — 500 imported rows means a 500-entry dropdown
  on every Room. Worse than invisible carriers, and worse the bigger the table. The narrowed M0 question is
  only whether key rows can be inserted *through the API* rather than only in the UI.
- Revit **2025 and 2026 are both .NET 8**, so one `net8.0-windows` build serves both. A later move to
  .NET 10 is forum talk, not published documentation — do not act on it yet.
- Revit has **no native Excel import**. Nothing is being reinvented here.
- Extensible Storage cannot be read by schedules — fine for the link record, useless for presentation.

## The Core layer (written 02-11-2026)

`Core/` is complete for M1 and M2 and has **no Revit API and no Excel library** — 145 off-screen checks
cover it. The files, and the one idea each exists for:

| File | What it settles |
|---|---|
| `Spreadsheet.cs` | `WorksheetTable` — a rectangle of **text**. Typing happens later, per column, so "this value will not fit that type" is reportable instead of silently coerced. `ISpreadsheetReader` is the seam `Excel/` implements. |
| `ColumnTypes.cs` | `ValueParser`: thousands separators, European decimals, accounting negatives `(250)`, percentages, currency symbols, unit suffixes (`25 mm`), yes/no words. `TypeGuess` picks the narrowest type that fits **every** value. |
| `ImportSettings.cs` | The five decisions as defaults, and **`ParameterPool`** — the heart of the design. |
| `ColumnMapping.cs` | `ColumnMap` (slot allocation, key suggestion) and `HeadingReconciler`, which matches by name first so moved columns are not reported as changes, then treats a heading in a known position as a rename. |
| `ImportValidation.cs` | Every case from ARCHITECTURE §9, each with a stable `Code` so the UI and the checks never match on prose. |
| `RowDiff.cs` | `SourceRows` + `RowDiffer`. Blank rows skipped. Matched by key, or `#n` by position. |
| `LinkRecord.cs` | The Extensible Storage record, as JSON. Deliberately holds **no row data**. Corrupt JSON returns null rather than throwing, so one bad record cannot stop a project's other links loading. |
| `DataStore.cs` | The whole Core/Revit boundary: `ICarrierStore`, `IScheduleBuilder`, `ILinkStore`, `CarrierRow`, `ApplyResult`. |

**Two non-obvious things in there, both deliberate:**
- **`PAC_RowHash`.** A refresh compares row hashes, not displayed values, because a number written into
  Revit does not read back as the text that was imported — `"0.10"` becomes `"0.1"` — so comparing values
  reports changes that never happened. The hash is computed from the spreadsheet text both times.
- **`TypeGuess` will not guess Yes/No for a column of 1s and 0s**, even though `1`/`0` parse as a flag.
  A column of 1s and 0s is far more likely to be counts, and guessing wrong loses the numbers. It only
  guesses Yes/No when at least one cell is written as a word.

## Decisions — defaults taken 02-11-2026 (Vineet to confirm)

Taken so work was not blocked; all five live in `Core/ImportSettings.cs`, each one a one-line change.

1. Carrier category — **Generic Models**.
2. Column caps — **24 text / 8 number / 4 integer / 4 length / 2 area / 2 yes-no** (48 parameters, fixed).
3. Rows — **warn at 5,000, refuse at 50,000**.
4. Row key — **optional**, row order when none, with a warning that says why that is fragile.
5. Shared parameter file — **PAC Tools owns its own**, so an outsourced build cannot corrupt TCPL's.

## Milestones

M0 spike (fixes the carrier decision) → M1 import happy path → M2 refresh → M3 ranges and named tables →
M4 manage-links, worksharing, ILRepack, guide. Detail in `docs/ARCHITECTURE.md §10`.

## Gotchas to remember

- The sample **Element Count** tool is the pipeline smoke test and the file to copy for a new tool. It can
  be deleted once the Excel tools exist; nothing depends on it.
- `tests/PacTools.Checks` references the **built DLL**, not the project, so the checks run on a machine with
  no Revit. Build the add-in first or the harness build fails with a clear message.
- `Revit/Icons.cs` deliberately does not import `Autodesk.Revit.DB`: `System.Windows.Point`/`Color` clash
  with `Autodesk.Revit.DB.Point`/`Color`.
- Revit locks the add-in DLL while it is open. Close Revit before `-p:DeployToRevit=true`.
- `RevitQueue.RunAsync` uses `RunContinuationsAsynchronously` so awaits resume **after** Revit's callback
  returns, never inside it. Don't "simplify" that away.
- `IsBusy` calls `CommandManager.InvalidateRequerySuggested()`; without it the buttons stay disabled after a
  refresh, because nothing else tells WPF to re-ask `CanExecute`.
- EPPlus is banned (Polyform Noncommercial from v5). Excel COM Interop is banned outright.

## The user guide

`docs/guide/PAC-Tools-Guide.pdf`, made by `docs/guide/build_guide.py` (reportlab) and committed, so a build
needs no Python. Copied next to the DLL at build and install; opened by **Help › Guide** (`GuideCommand`),
which also works with no project open. When a tool changes, update the text there and rebuild the PDF.

## Installing

`install\Install-PACTools.bat` (double-click). Checks Revit is closed, finds Revit and the .NET SDK,
builds, copies to `%AppData%\Autodesk\Revit\Addins\2025\`, then hash-verifies installed == built and
checks the manifest's `Assembly` path actually resolves (if it does not, Revit loads nothing and says
nothing — that check exists because the failure is silent).

The user asked for PAC Tools as "a new tool palette" on 02-11-2026 and confirmed they meant **its own
ribbon tab**, not a dockable pane. The tab already existed in the scaffold, so nothing was added for it.
A dockable pane (`IDockablePaneProvider`) was offered and declined for now — it is still the natural home
for "Manage links" in M4 if they change their mind.

## Repository

- Remote: `https://github.com/vikramkushawah/PAC-Tools` — **private**, branch `main`.
- Separate from TCPL BIM Tools on purpose: outsourced developers get this repository and nothing else.
  Never copy licence code, the licence generator, or any private key into it.
- Commit and push after every change, same rule as the TCPL repository.
