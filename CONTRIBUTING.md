# Working on PAC Tools

For anyone — in-house or outsourced — writing code in this repository.
Read **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** first; it decides what the code has to be.

---

## 1. The two rules

### Revit must never freeze
A Revit add-in that blocks Revit's main thread makes Revit unusable. So:

- Show a window with `ModelessWindows.Show(...)`. **Never `Window.ShowDialog()`.**
- Every call into the Revit API from a window goes through the `RevitQueue`
  (`await _runner.RunAsync(() => ...)`). A modeless window is not in the Revit API context, and calling
  the API from one throws — or, worse, corrupts the document.
- Batch what you ask for. One `RunAsync` that reads everything beats six that each read one thing: each
  one is a separate `ExternalEvent` round trip.

### `Core/` has no Revit API and no Excel library
`Core/` is plain C#. The Revit API lives only in `Revit/`; the Excel library only in `Excel/`.
Anything that can be decided without asking Revit — mapping, diffing, parsing, validation, formatting —
belongs in `Core/`, behind an interface that `Revit/` implements.

The payoff is `tests/PacTools.Checks`: the logic and the view models run in about a second, on any machine,
with no Revit and no Excel. Breaking this rule means every change has to be tested by hand in Revit, and
the project slows to a crawl. It is not a style preference.

---

## 2. Adding a new tool

1. **`Core/`** — the interface for what it needs from Revit (`IThingService`) and the logic (pure C#).
2. **`Revit/`** — implement the interface (`RevitThingService`).
3. **`UI/`** — a view model taking `(IThingService, IRevitRunner)` and a XAML window.
4. **`Revit/ThingCommand.cs`** — copy `ElementCountCommand.cs`; give it a unique `WindowKey`.
5. **`Revit/App.cs`** — one more `PushButtonData`. Button name: `Brand.ButtonPrefix + "Thing"`.
6. **`tests/PacTools.Checks`** — a fake service and checks for the logic *and* the view model, including
   what happens when the service throws.

## 3. Transactions

Every model change is wrapped in a transaction, and **one user action is one transaction**, so one Ctrl+Z
undoes it:

```csharp
// Inside the RevitQueue - never from a window's event handler directly.
using (var t = new Transaction(doc, "PAC Tools - import 'Door schedule'"))
{
    t.Start();
    //  ... all the changes for this one user action ...
    if (nothingChanged) { t.RollBack(); return Result.Unchanged; }
    t.Commit();
}
```

- Name it so the user recognises it in the undo list: `"PAC Tools - <what the user did>"`.
- Roll back when nothing actually changed, so the undo list stays clean.
- Never open a transaction in a loop over rows. One transaction, all the rows.
- Use `SubTransaction` when one row may need to be abandoned without losing the rest.
- Read the model again at the start of an action. The user may have changed it since the window opened.

## 4. Errors the user can act on

Every failure the user can cause gets its own message saying what happened and what to do about it. The
cases are enumerated in [docs/ARCHITECTURE.md §9](docs/ARCHITECTURE.md) — that table is the specification,
not a suggestion.

- Validate **before** touching the model. A wizard that reports 12 bad cells on screen beats a transaction
  that half-succeeds.
- Problems the user caused (file moved, heading renamed, a cell that will not parse) go in the window's
  problem box — not a `TaskDialog`, and never an unhandled exception.
- `PickProblemException` / `WrongProjectException` are for exactly this: a message, not a crash.
- A view model's `RefreshAsync`-style method should **never throw**. Catch, set `ProblemText`, carry on.
- Unexpected exceptions go to `App.Log` *and* get a plain-language message. Never show a raw stack trace.
- A bad cell does not abort an import. Leave it empty, count it, report it.

## 5. Style

- 4 spaces, CRLF, ~130 column lines. `.editorconfig` has the rest; please have your editor honour it.
- `using` directives outside the namespace, `System` first.
- Public members get an XML doc comment — the build generates `PacTools.xml`.
- Comments say **why**, not what. `// one transaction so the import is one undo step` is useful;
  `// start the transaction` is not.
- Explicit types over `var` for anything non-obvious. Match the surrounding code.
- No `#region`. No unexplained magic numbers.
- British spelling in user-facing text ("colour", "metre", "licence" as a noun).

## 6. Dependencies

Ask before adding a NuGet package. Every package that loads inside Revit can clash with another add-in's
copy of the same library — Revit shares one load context. The ones already decided:

| Package | Licence | What for |
|---|---|---|
| ClosedXML | MIT | `.xlsx` / `.xlsm`, named tables and ranges |
| ExcelDataReader | MIT | legacy `.xls` |

**Not** EPPlus (v5+ is Polyform Noncommercial — unsuitable for commercial work), and **never** Excel COM
Interop. Before release, third-party assemblies are merged into `PacTools.dll` with ILRepack and
internalised, so there is no public assembly for another add-in to collide with.

## 7. Git

- Branch off `main`: `feature/<short-name>`, `fix/<short-name>`.
- Small commits; the subject line says what changed and why, in the imperative:
  `"refresh: keep the old value when a cell will not parse"`.
- Run the checks before every push. Do not push a red build.
- Open a pull request to `main`; say what you tested in Revit and what you did not.
- Never commit: `bin/`, `obj/`, client spreadsheets, credentials, or anything in `.gitignore`.
- Never change `AddInId` or `VendorId` in `PacTools.addin`. Revit identifies the add-in by `AddInId`:
  a new one makes every installed copy load alongside the old one.

## 8. Before you call something done

- [ ] `tests/PacTools.Checks` all pass.
- [ ] Built with **0 warnings**.
- [ ] Tried in a real Revit project: it works, and Revit stayed usable while the window was open.
- [ ] One Ctrl+Z undoes the whole action.
- [ ] Every error path in §4 tried on purpose at least once.
- [ ] `CLAUDE.md` updated with what changed and anything surprising you learned.
