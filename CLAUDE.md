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
| Built / run in Revit | **No.** Written in a Linux container with no .NET SDK and no Revit — **never compiled.** The first job on a Windows machine is `dotnet build` and fixing whatever it says. |
| Excel work | **Not started.** Designed only. |
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
  parameters are the columns. Revit's help says key schedules take **instance parameters only**, and rows are
  added in the UI with *Insert Data Row*. **No API row-insertion method is documented — this is the M0 spike.**
- Revit **2025 and 2026 are both .NET 8**, so one `net8.0-windows` build serves both. A later move to
  .NET 10 is forum talk, not published documentation — do not act on it yet.
- Revit has **no native Excel import**. Nothing is being reinvented here.
- Extensible Storage cannot be read by schedules — fine for the link record, useless for presentation.

## Open decisions (asked of Vineet 01-11-2026, not yet answered)

1. Carrier category — Generic Models, or something less used?
2. Column caps — 24 text / 8 number / 4 integer / 2 yes-no enough?
3. Row warning threshold — 5,000 proposed.
4. Row key — a required key column, or row order?
5. Shared parameter file — PAC Tools owns its own `.txt`, or append to TCPL's existing one?

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

## Repository

- Remote: `https://github.com/vikramkushawah/PAC-Tools` — **private**, branch `main`.
- Separate from TCPL BIM Tools on purpose: outsourced developers get this repository and nothing else.
  Never copy licence code, the licence generator, or any private key into it.
- Commit and push after every change, same rule as the TCPL repository.
