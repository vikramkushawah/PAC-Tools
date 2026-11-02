# PAC Tools — Excel to Revit schedules: design assessment

Status: **proposal, awaiting approval.** Nothing in here is built yet beyond the scaffold.
Written 01-11-2026. Decisions marked **[M0]** must be confirmed against a live Revit before they are relied on.

---

## 1. The one fact that decides the whole design

A Revit schedule is **not a table you can fill in**. It is a *saved query* over Revit's own database:

- `ViewSchedule.CreateSchedule(doc, categoryId)` binds the schedule to a **category**.
- Its columns can only be chosen from `ScheduleDefinition.GetSchedulableFields()` — which is
  *the parameters that actually exist on that category's elements*, plus derived fields (count,
  percentage, formula, combined parameter).
- Its rows are **one per element** (or one per group when itemisation is off). There is no "add a row".
- `TableSectionData.SetCellText` **throws `InvalidOperationException` for cells in a standard schedule
  body section.** The body is read-only by design: "the cell contents of `TableSectionData` are generated
  from the `ScheduleDefinition` and parameters." Only the *header* section is writable.

So the obvious shortcut — make a schedule and paste the spreadsheet into its cells — **is not available**.
This is a hard API constraint, not a difficulty to engineer around.

**It follows that:** if the data is to appear in a native Revit schedule, the data must live in
**parameters on Revit elements**. There is no third way.

That turns the brief into a storage question, which is what the rest of this document answers.

---

## 2. Options considered

| # | Approach | Verdict |
|---|---|---|
| A | Draw the table in a drafting view (TextNotes / detail lines) | **Rejected as the main path** |
| B | Native schedule over purpose-made "carrier" elements, one per row | **Recommended** |
| C | Key schedule (`CreateKeySchedule`) — abstract key elements, no model geometry | **Strong alternative, spike it [M0]** |
| D | Extensible Storage only | **Rejected alone — but it is the right place for the link record** |

### A. Drafting view with a drawn table — rejected
Create a `ViewDrafting` and lay the spreadsheet out as text.

- **For:** any shape of data, exact fidelity, refresh = wipe and redraw, no shared parameters, no model elements.
- **Against:** it is **not a schedule**. No Revit sorting, filtering, grouping or totals; it does not appear
  under Schedules/Quantities in the Project Browser; it cannot feed any schedule-driven workflow. And a
  500 × 20 table means 10,000 TextNote elements — model bloat and painful view performance.
- Worth keeping as an **optional extra** later for awkwardly shaped data (merged cells, notes blocks),
  clearly labelled as a picture of a table rather than a schedule. Not the deliverable.

### B. Carrier elements + native schedule — **recommended**
One geometry-free Revit element per spreadsheet row; one shared parameter per spreadsheet column; a normal
`ViewSchedule` over that category, filtered to this dataset.

- **For:** a genuine Revit schedule with the full feature set — fields, filters, sorting/grouping, totals,
  formatting, appearance, placement on sheets, Revit's own export. Rows scale to thousands. The data is in
  parameters, so it can later be tagged, filtered in views, or driven into real elements.
- **Against:** it puts real elements in the model and shared parameters in the project. Both are managed
  costs, addressed in §4 and §5.

### C. Key schedule — spike it
`ViewSchedule.CreateKeySchedule(doc, categoryId)` creates a schedule of **abstract "key" elements**: each
row is an element carrying a set of parameters, with no geometry at all.

- **For:** no model elements, nothing in 3D, nothing to clash or to hide — conceptually the closest thing
  Revit has to "rows of data".
- **Against / unknown:**
  - A mandatory **Key Name** column that cannot be removed.
  - Revit's own help states key schedules can include **instance parameters only**.
  - Rows are added in the UI with *Insert Data Row*; **no API row-insertion method is documented**. Whether
    rows can be created programmatically is the single biggest unknown in this design. **[M0]**
  - Still category-bound, so the shared-parameter cost of option B does not go away.
- If the M0 spike shows rows can be created reliably, key schedules become the better carrier for
  option B and the rest of this design is unchanged — the carrier is deliberately behind an interface.

### D. Extensible Storage alone — rejected as the presentation layer
Extensible Storage holds arbitrary structured data invisibly on a `DataStorage` element, with no parameters
and no model pollution. But **Revit schedules cannot read Extensible Storage.** It can store the data; it
can never show it as a schedule.

It is, however, exactly right for the **link record** — see §4.

---

## 3. Recommended architecture

**Extensible Storage for the link + carrier elements for the rows + a native `ViewSchedule` for the view.**

```
   Excel workbook  ──read──▶  WorksheetTable        (plain C#: headers + rows, no Revit, no Excel COM)
   (.xlsx/.xlsm)                   │
                                   ▼
                            ColumnMapping           (spreadsheet column ──▶ Revit parameter + type)
                                   │
                    ┌──────────────┴───────────────┐
                    ▼                              ▼
          carrier elements                  link record
      one per spreadsheet row           (Extensible Storage on
      values in shared parameters        a DataStorage element)
                    │                              │
                    ▼                              │
            ViewSchedule  ◀────── points at ───────┘
      (category + filter on the dataset id)
```

Each import produces: *n* carrier elements, 1 link record, 1 schedule view. All three are created in a
**single transaction**, so an import or a refresh is **one Ctrl+Z**.

### The carrier
- A **geometry-free family instance** in a configurable category — default **Generic Models**.
  No geometry means nothing renders, nothing clashes, nothing shows in any view.
- All carriers placed at the project base point; their location is meaningless.
- Put on a dedicated **workset** ("PAC Data") when the model is workshared.
- Every carrier stamped with `PAC_DataSetId` so (a) the schedule can filter to exactly this dataset and
  (b) every other schedule in the project can filter them *out* with one rule.
- Behind `ICarrierStore`, so the M0 spike can swap in key schedules without touching anything above it.

### The column trick that keeps the project clean
The naive approach — one shared parameter per spreadsheet column — pollutes the project without limit:
import three 20-column sheets and the Generic Models category carries 60 new parameters, **on every
Generic Model in the project**, because Revit parameter bindings are per-category and project-wide.

Instead, use a **fixed pool of generically named shared parameters** and rename them *per schedule*:

- `PAC_T01`…`PAC_T24` (Text), `PAC_N01`…`PAC_N08` (Number), `PAC_I01`…`PAC_I04` (Integer),
  `PAC_Y01`…`PAC_Y02` (Yes/No) — 38 parameters, created once, **however many spreadsheets are imported**.
- `ScheduleField.ColumnHeading` is settable, so the schedule shows **"Door Reference"** while the
  underlying parameter is `PAC_T03`. The user never sees the pool name.
- A spreadsheet column is therefore bound to a pool slot, and the *display* name is schedule-level data.

**This makes the "changed column header" case trivial:** renaming a spreadsheet heading changes a string in
the link record and a `ColumnHeading`, not a parameter binding. No re-binding, no data migration.

**Cost:** a hard cap on columns per type (24 text columns by default). Configurable, and a sheet wider than
that is a reasonable thing to refuse with a clear message rather than to support.

---

## 4. The link record (Extensible Storage)

One `DataStorage` element per import, carrying a versioned schema:

| Field | Why |
|---|---|
| `SchemaVersion` | so a future build can migrate or refuse politely |
| `DataSetId` (GUID) | ties carriers, schedule and record together |
| `WorkbookPathAbsolute` | where it was last read from |
| `WorkbookPathRelative` | relative to the .rvt, so moving the whole project folder still works |
| `WorksheetName`, `RangeRef`, `NamedTable` | what was read |
| `HeaderRowIndex`, `FirstDataRow` | how it was read |
| `ColumnMap` (JSON) | spreadsheet heading → pool parameter GUID, data type, unit, display name, order |
| `KeyColumn` | which column identifies a row across refreshes (optional) |
| `ContentHash` | SHA-256 of the extracted range: answers "has it changed?" without a full re-read |
| *(per row, on the carrier)* `PAC_RowHash` | a hash of that row's source cells. A refresh compares hashes, never displayed values — a number written to Revit does not read back as the text that was imported ("0.10" becomes "0.1"), so comparing values reports changes that never happened |
| `SourceLastWriteUtc` | cheap first check before hashing |
| `LastRefreshUtc`, `LastRefreshBy` | audit, and useful in a workshared model |
| `ScheduleViewId` | the schedule this link feeds |

**No row snapshot is stored.** The carriers *are* the previous state: a refresh reads their parameter values
and diffs against the new spreadsheet read. That keeps the record small and bounded, and removes any
possibility of the stored snapshot and the model disagreeing.

---

## 5. Honest tradeoffs

| Cost | Size | Mitigation |
|---|---|---|
| Real elements enter the model | one per row | geometry-free, dedicated workset, stamped and filterable |
| Shared parameters enter the project, category-wide | fixed 38, not per-column | the pool + `ColumnHeading` trick (§3) |
| Column count is capped | 24 text / 8 number / 4 int / 2 bool | configurable; refuse wider sheets with a clear message |
| Row count is practically capped | comfortable to ~5,000; slow past ~20,000 | warn above a threshold, offer to import a range instead |
| Carriers inflate Generic Models counts | visible in other schedules | documented one-rule filter; consider a less-used category |
| A third-party Excel library loads inside Revit | version clash with other add-ins | internalise it (ILRepack) before release — see §7 |
| Deleting the schedule does not delete the carriers | orphan data | a **Manage links** window that unlinks and cleans up |
| Two people refreshing a workshared model | element borrowing conflicts | refresh requires ownership of the carriers; report clearly |

**What this design does not pretend to be:** a live, two-way Excel link. It is a one-way import with an
explicit, user-triggered refresh. Writing Revit changes back to the spreadsheet is out of scope (§9).

---

## 6. Supported versions

**Revit 2025 and 2026.** Both are built on .NET 8, so one `net8.0-windows` build serves both, and the
schedule API used here is unchanged between them. Revit 2024 and older run on .NET Framework 4.8 and would
need a second target — **out of scope unless asked for.**

> Autodesk has signalled a future move to .NET 10 for a later release. That is currently forum-level
> information, not published API documentation. The project stays on .NET 8 and re-tests when Autodesk
> publishes release notes; a .NET 8 add-in is expected to load in a .NET 10 Revit, but this is unverified.

**Excel: no Excel installation required**, and **no COM Interop** — Interop is slow, needs a licensed Excel,
breaks on bitness mismatches and leaves orphaned `EXCEL.EXE` processes inside Revit. Instead:

| Format | Support | How |
|---|---|---|
| `.xlsx`, `.xlsm` (Excel 2007 → Microsoft 365) | **full**, incl. named tables and named ranges | ClosedXML (MIT) |
| `.csv` | full, single table | built-in reader |
| `.xls` (97–2003) | read-only, flagged as legacy | ExcelDataReader (MIT), or ask the user to convert |
| `.xlsb` (binary) | **not supported** — clear message asking the user to save as `.xlsx` | — |
| Google Sheets, SharePoint/OneDrive URLs | not in scope; the file must be reachable on disk or a UNC path | — |

Both libraries are MIT. EPPlus is deliberately **not** used: v5+ is Polyform Noncommercial, which does not
suit a consultancy's commercial work.

---

## 7. Code layers

The scaffold's rule holds: **`Core/` contains no Revit API and no Excel library.** That is what makes the
logic — mapping, diffing, validation, type parsing — testable in seconds without Revit.

```
Core/        WorksheetTable, ColumnMapping, TypeParsing, RowDiff, Validation, LinkRecord
             ISpreadsheetReader, ICarrierStore, IScheduleBuilder     (no Revit, no Excel)
Excel/       ClosedXmlReader, CsvReader, LegacyXlsReader             (implements ISpreadsheetReader)
Revit/       CarrierStore, ScheduleBuilder, SharedParameterPool,
             LinkStorage (Extensible Storage), RevitQueue            (the only Revit API code)
UI/          the import wizard, Manage links, view models            (no Revit API)
```

**Excel library isolation:** ClosedXML and its dependencies load into Revit's shared load context, where
another add-in may already have loaded a different version. Before any release, merge them into
`PacTools.dll` with ILRepack and internalise them, so there is no public assembly to clash over. Tracked
as a release blocker, not an M1 task.

---

## 8. User workflow

**Import** (a 4-step wizard, modeless, Revit stays usable throughout):

1. **Workbook** — pick the file. Recent files listed. Validate: exists, readable, not locked, a supported format.
2. **Worksheet and range** — list the worksheets, the named tables and the named ranges; or type an A1
   range. Show a **live preview grid of the first 20 rows** so the user can see what Revit will get.
   Tick "the first row is headings".
3. **Columns** — a row per spreadsheet column: include?, heading, data type (Text / Number / Integer /
   Length / Area / Yes-No), unit where relevant, and which column is the row key. Bad cells are flagged
   here, **before** anything touches the model.
4. **Create** — name the schedule, choose the category and workset, see the summary
   ("312 rows, 11 columns, 3 cells could not be read"), then Create.

**Refresh** — from the ribbon or from Manage links. Compares, then shows
*"18 changed, 4 added, 2 no longer in the spreadsheet — apply?"* with the detail available before anything
is applied. One transaction, one undo step. Nothing is ever deleted without that confirmation.

**Manage links** — every link in the project: source, worksheet, rows, last refreshed, status
(OK / file moved / file changed / headings changed). Refresh, Refresh all, Relocate, Unlink (keep the
elements), Delete (remove carriers and schedule).

---

## 9. Validation and messages

Every one of these is a real case with its own message — no generic "an error occurred":

| Situation | What the user is told |
|---|---|
| File missing / moved | "The workbook is no longer at <path>. Locate it, or unlink." + **Locate** button |
| File open and locked | "Excel has the workbook open with unsaved changes. Save and close it, then refresh." |
| `.xlsb`, or not a workbook | "PAC Tools reads .xlsx, .xlsm, .xls and .csv. Save this workbook as .xlsx." |
| Worksheet renamed or gone | "The worksheet 'Doors' is not in the workbook any more. It now contains: …" + re-pick |
| Heading renamed | "The heading in column D changed from 'Ref' to 'Reference'. Keep the mapping, or remap?" |
| Column removed | "Column 'Fire rating' is gone. Keep the Revit column (values kept), or drop it?" |
| Columns added | "2 new columns found. Add them to the schedule?" (never added silently) |
| Blank or duplicate headings | named per column, with the fix, before the model is touched |
| Too many columns | "This sheet has 31 text columns; the limit is 24. Choose which to import." |
| Cell will not parse as its type | listed per cell with the row number; the cell is left empty and reported, the import is not aborted |
| Number with units in the cell ("25 mm") | parsed where unambiguous, reported where not |
| Merged cells in the range | "Rows 14–16 are merged in column B. Only the first row's value is read." |
| Duplicate row keys | "'D-101' appears in rows 7 and 92. Row keys must be unique, or choose a different key column." |
| Empty range | "The range is empty — nothing to import." |
| Rows above the warning threshold | "This will add 12,400 elements to the model. That will slow it down. Continue, or import a range?" |
| Carrier borrowed by someone else | "3 rows are owned by <user>. Ask them to relinquish, then refresh." |
| Project read-only / not a project | refused up front, before the wizard opens |

---

## 10. Milestones

| | Scope | Output |
|---|---|---|
| **M0** | **Spike, ~half a day.** In live Revit 2025: bind pooled shared parameters to Generic Models and confirm they appear in `GetSchedulableFields()`; create a geometry-free carrier and confirm it is invisible; build a `ViewSchedule`, filter it by `PAC_DataSetId`, set `ColumnHeading`, sort, place on a sheet; test whether key-schedule rows can be created via the API; measure 5,000 carriers. | A findings note that **fixes the carrier decision**. Nothing else starts first. |
| **M1** | Import, happy path + the validation in §9 that applies before the model is touched. ClosedXML reader, `WorksheetTable`, column mapping, parameter pool, carrier creation, schedule creation, link record written. | A workbook becomes a working Revit schedule. |
| **M2** | Refresh. Hash/last-write check, diff against carriers, the confirm-before-apply dialog, header-change and file-moved handling, one undo step. | The link is maintainable. |
| **M3** | Named tables, named ranges, typed A1 ranges, range preview, several links per project, typed columns and units. | Requirement 3 complete. |
| **M4** | Manage links window, worksharing behaviour, ILRepack isolation, logging, user guide, installer. | Releasable. |

Each milestone ends with off-screen checks passing (`tests/PacTools.Checks`) and a Revit smoke test.

---

## 10a. Findings so far (02-11-2026, from Autodesk product documentation)

Checked against Autodesk's own help, not from memory. Two things moved:

**Key schedules are more viable than the API reference alone suggested.** Revit's *Modify a Schedule* page
says: *"add a data row to a room schedule, area schedule, **key schedule**, space schedule, or sheet
list… Enter values as desired. This function is useful for defining elements before placing them in the
model."* And: *"To edit a cell, click in it… or enter text."* So body cells **are** editable for those
schedule types — the API's prohibition is specifically on *"standard schedule body sections"*, and a key
schedule is not one. *Create a Key Schedule* also confirms shared parameters work there, with the
restriction already noted: **"Only Instance parameters can be included in a key schedule."**

**But a new cost appears, and it is the reason the recommendation does not change.** Key rows become
selectable key values in the instance properties of **every element of that category**. Import 500 rows
against Rooms and every Room in the project gets a 500-entry style dropdown. For a data table that is
worse than invisible carrier elements, and it gets worse the bigger the table. So:

> **Key schedules stay the alternative, not the primary — suited to small tables (tens of rows), where
> the dropdown is harmless. `ICarrierStore` keeps both possible.** The remaining unknown is narrower than
> before: whether rows can be inserted *through the API* rather than only in the UI. Still M0.

**Filtering is confirmed adequate.** *Filter Data in a Schedule*: filters support text, number, integer,
length, area, volume, yes/no **and key schedule parameters**, up to **8 filters**. The design needs one
(`PAC_DataSetId`), so there is ample room.

**Also confirmed:** Revit has no native Excel import — *Export a Schedule* goes one way only, to delimited
text. Nothing here is reinventing an existing feature.

## 11. Decisions — defaults taken 02-11-2026, still open to change

All five are now implemented as **defaults in `Core/ImportSettings.cs`**, so work could continue without
blocking. Each is a one-line change, and each is commented in that file with the reasoning. Vineet should
confirm or correct them before M1 ships:

| # | Decision | Default taken | Why, and what it costs |
|---|---|---|---|
| 1 | Carrier category | **Generic Models** | Every project has it and it schedules cleanly. Cost: carriers appear in Generic Models counts elsewhere, which one filter removes. |
| 2 | Column caps | **24 text, 8 number, 4 integer, 4 length, 2 area, 2 yes/no** | 44 pool parameters + 4 bookkeeping = **48, fixed, forever**. Cost: a wider sheet is refused with a clear message. |
| 3 | Row warning / refusal | **warn at 5,000, refuse at 50,000** | Past ~20,000 the model suffers; the hard limit stops someone importing a database by accident. |
| 4 | Row key | **optional; row order when none** | Not blocking an import on it. The wizard recommends a key column and says plainly that row order re-labels everything below an inserted row. |
| 5 | Shared parameter file | **PAC Tools owns `PAC_Tools_Shared_Parameters.txt`** | An outsourced build can then never corrupt TCPL's office file. Cost: a second file to keep with the project. |

### The original list, for reference

1. Carrier category — Generic Models, or something less used?
2. Column caps — are the numbers above enough for TCPL's real spreadsheets?
3. Row warning threshold.
4. Row key — required, or is row order acceptable?
5. Shared parameter file — own file, or append to TCPL's?


## 12. Deliberately out of scope

Writing Revit changes back to Excel · live/automatic refresh on file change · Google Sheets and
SharePoint URLs · importing *into* existing model elements (that is a different tool — matching
spreadsheet rows to real doors or rooms) · charts, images and cell formatting · merged-cell fidelity.

---

## Sources for the API facts above

- [`TableSectionData.SetCellText` — Revit 2026 API reference](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/1c7a694b-73c0-8d15-7817-5620b6e83098.htm)
  (`InvalidOperationException` — forbidden for cells in standard schedule body sections)
- [Schedule Classes — Revit API Developers Guide](https://help.autodesk.com/cloudhelp/2024/ITA/Revit-API/files/Revit_API_Developers_Guide/Basic_Interaction_with_Revit_Elements/Views/View_Types/TableView/Revit_API_Revit_API_Developers_Guide_Basic_Interaction_with_Revit_Elements_Views_View_Types_TableView_Schedule_Classes_html.html)
  (body section read-only; contents generated from the definition and parameters)
- [`ViewSchedule.CreateKeySchedule` — Revit 2026 API reference](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/98a8f20f-c442-3e2e-02fb-e09dabee0fbe.htm)
- [Creating a Key Schedule — Revit help](https://help.autodesk.com/view/RVT/2025/ENU/?guid=GUID-75B3B9C4-6899-4DC9-8424-DFC901701B48) (Insert Data Row; instance parameters only)
- [Key Schedule + Revit API — Autodesk Community](https://forums.autodesk.com/t5/revit-api-forum/key-schedule-revit-api/td-p/5449134) (each key row is an element whose parameters are the columns)
- [Can't insert text into schedule cell — Autodesk Community](https://forums.autodesk.com/t5/revit-api-forum/can-t-insert-text-into-schedule-cell/td-p/7984275)
- [Add a row to a schedule — Autodesk Community](https://forums.autodesk.com/t5/revit-api-forum/add-a-row-to-a-schedule/td-p/7984145)
- [Shared parameter fields in a schedule need `Definition.InsertField` — Autodesk Community](https://forums.autodesk.com/t5/revit-api-forum/how-to-add-shared-parameter-fields-in-schedule-and-how-to/td-p/11215698)
- [Category-wide effect of `ParameterBindings` — pyRevit forums](https://discourse.pyrevitlabs.io/t/add-shared-parameters/3534)
- [Revit has no built-in Excel import](https://www.pirros.com/revit/how-to/import-excel)
- [FireRating SDK sample: shared parameter → Excel → back again](https://jeremytammik.github.io/tbc/a/0828_export_param_excel.htm)
- [Revit 2026 release announcement — 2025 and 2026 are .NET 8](https://blog.autodesk.io/revit-2026-release-announcement/)
- [What's New in Revit API 2025 — rebuild on .NET 8](https://rvtdocs.com/2025/whats-new/)
- [.NET 10 migration discussion (unconfirmed, forum)](https://forums.autodesk.com/t5/revit-api-forum/revit-2025-2026-migration-to-net-10-compatibility-and-deployment/td-p/14116833)
