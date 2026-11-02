// PAC Tools checks - the Excel-to-schedule logic, with no Revit and no Excel.
using System;
using System.Collections.Generic;
using System.Linq;
using PacTools.Core;

namespace PacTools.Checks
{
    internal static class ImportChecks
    {
        /// <summary>A small table: headings in row 3, data from row 4.</summary>
        private static WorksheetTable Doors()
        {
            return new WorksheetTable("Doors", "A3:D7",
                new[] { "Door ref", "Width", "Fire rated", "Notes" },
                new[]
                {
                    new[] { "D-101", "900",   "Yes", "Entrance" },
                    new[] { "D-102", "1,200", "No",  "" },
                    new[] { "D-103", "900",   "Yes", "Lobby" }
                },
                firstDataRowNumber: 4);
        }

        internal static void Run()
        {
            Console.WriteLine();
            Console.WriteLine("Excel to schedule");

            Formats();
            Table();
            Parsing();
            Guessing();
            Pool();
            Mapping();
            Reconciling();
            Validating();
            Diffing();
            Records();
        }

        private static void Formats()
        {
            Program.Check("xlsx is recognised", WorkbookFormats.FromPath(@"C:\a\b.xlsx") == WorkbookFormat.Xlsx);
            Program.Check("case does not matter", WorkbookFormats.FromPath("B.XLSM") == WorkbookFormat.Xlsm);
            Program.Check("xls is legacy", WorkbookFormats.IsLegacy(WorkbookFormats.FromPath("old.xls")));
            Program.Check("csv is read", WorkbookFormats.FromPath("list.csv") == WorkbookFormat.Csv);
            Program.Check("xlsb is refused", WorkbookFormats.FromPath("b.xlsb") == WorkbookFormat.Unsupported);
            Program.Check("the refusal says to save as xlsx",
                WorkbookFormats.UnsupportedMessage("b.xlsb").Contains("save it as .xlsx"));
            Program.Check("the refusal names what is read",
                WorkbookFormats.UnsupportedMessage("b.pdf").Contains(".xlsx, .xlsm, .xls and .csv"));
        }

        private static void Table()
        {
            WorksheetTable t = Doors();
            Program.Check("the table knows its size", t.RowCount == 3 && t.ColumnCount == 4);
            Program.Check("headings are trimmed", t.Headings[0] == "Door ref");
            Program.Check("row numbers are Excel's", t.SpreadsheetRowNumber(0) == 4 && t.SpreadsheetRowNumber(2) == 6);
            Program.Check("a cell outside the table is empty, not a crash", t.Cell(99, 99) == string.Empty);
            Program.Check("a column can be read down", t.Column(0).SequenceEqual(new[] { "D-101", "D-102", "D-103" }));
            Program.Check("the preview is capped", t.Preview(2).Count == 2);

            WorksheetTable ragged = new WorksheetTable("S", "A1:C2", new[] { "a", "b", "c" },
                new[] { new[] { "1" }, new[] { "1", "2", "3", "4" } }, 2);
            Program.Check("short rows are padded", ragged.Rows[0].Count == 3 && ragged.Rows[0][2] == string.Empty);
            Program.Check("long rows are trimmed", ragged.Rows[1].Count == 3);

            Program.Check("an empty table says so", new WorksheetTable("S", "A1", new string[0], null, 1).IsEmpty);
            Program.Check("the same content hashes the same", Doors().ContentHash() == Doors().ContentHash());

            WorksheetTable changed = new WorksheetTable("Doors", "A3:D7",
                new[] { "Door ref", "Width", "Fire rated", "Notes" },
                new[] { new[] { "D-101", "901", "Yes", "Entrance" } }, 4);
            Program.Check("different content hashes differently", Doors().ContentHash() != changed.ContentHash());
        }

        private static void Parsing()
        {
            Program.Check("blank is allowed", ValueParser.Parse("", ColumnDataType.Integer).IsBlank);
            Program.Check("blank is not a failure", ValueParser.Parse("   ", ColumnDataType.Area).Ok);
            Program.Check("text takes anything", ValueParser.Parse("anything at all", ColumnDataType.Text).Ok);

            Program.Check("a plain number reads", ValueParser.Parse("900", ColumnDataType.Number).Number == 900);
            Program.Check("thousands separators read", ValueParser.Parse("1,200", ColumnDataType.Number).Number == 1200);
            Program.Check("decimals read", Math.Abs(ValueParser.Parse("2.5", ColumnDataType.Number).Number - 2.5) < 1e-9);
            Program.Check("european decimals read",
                Math.Abs(ValueParser.Parse("1.234,5", ColumnDataType.Number).Number - 1234.5) < 1e-9);
            Program.Check("accounting negatives read", ValueParser.Parse("(250)", ColumnDataType.Number).Number == -250);
            Program.Check("a percentage becomes a fraction",
                Math.Abs(ValueParser.Parse("12.5%", ColumnDataType.Number).Number - 0.125) < 1e-9);
            Program.Check("a currency symbol is ignored", ValueParser.Parse("\u20b9 1,500", ColumnDataType.Number).Number == 1500);

            ParsedCell mm = ValueParser.Parse("25 mm", ColumnDataType.Length);
            Program.Check("a unit in the cell is read", mm.Ok && mm.Number == 25 && mm.Unit == "mm");
            Program.Check("a plain Number carries no unit", ValueParser.Parse("25 mm", ColumnDataType.Number).Unit == null);
            ParsedCell sqm = ValueParser.Parse("12.5 sqm", ColumnDataType.Area);
            Program.Check("an area unit is read", sqm.Ok && sqm.Unit == "sqm");

            Program.Check("text in a number column fails", !ValueParser.Parse("about 900", ColumnDataType.Number).Ok);
            Program.Check("the failure names the value",
                ValueParser.Parse("about 900", ColumnDataType.Number).Problem.Contains("'about 900'"));
            Program.Check("a decimal in a whole-number column fails",
                !ValueParser.Parse("2.5", ColumnDataType.Integer).Ok);
            Program.Check("and it says to use Number instead",
                ValueParser.Parse("2.5", ColumnDataType.Integer).Problem.Contains("Number column"));
            Program.Check("a whole number in a whole-number column reads",
                ValueParser.Parse("12", ColumnDataType.Integer).Number == 12);

            Program.Check("yes reads as true", ValueParser.Parse("Yes", ColumnDataType.YesNo).Flag);
            Program.Check("true reads as true", ValueParser.Parse("TRUE", ColumnDataType.YesNo).Flag);
            Program.Check("no reads as false", !ValueParser.Parse("no", ColumnDataType.YesNo).Flag);
            Program.Check("nonsense in a yes/no column fails",
                !ValueParser.Parse("maybe", ColumnDataType.YesNo).Ok);
            Program.Check("and it says what is accepted",
                ValueParser.Parse("maybe", ColumnDataType.YesNo).Problem.Contains("Yes/No"));
        }

        private static void Guessing()
        {
            Program.Check("all words is text", TypeGuess.Guess(new[] { "Entrance", "Lobby" }) == ColumnDataType.Text);
            Program.Check("all whole numbers is a whole number",
                TypeGuess.Guess(new[] { "900", "1200" }) == ColumnDataType.Integer);
            Program.Check("decimals make it a number",
                TypeGuess.Guess(new[] { "900", "2.5" }) == ColumnDataType.Number);
            Program.Check("yes and no is a tick box",
                TypeGuess.Guess(new[] { "Yes", "No", "Yes" }) == ColumnDataType.YesNo);
            Program.Check("1s and 0s stay whole numbers, not tick boxes",
                TypeGuess.Guess(new[] { "1", "0", "1" }) == ColumnDataType.Integer);
            Program.Check("one odd value falls back to text",
                TypeGuess.Guess(new[] { "900", "1200", "see note" }) == ColumnDataType.Text);
            Program.Check("an empty column is text", TypeGuess.Guess(new[] { "", "  " }) == ColumnDataType.Text);
            Program.Check("blanks are ignored when guessing",
                TypeGuess.Guess(new[] { "900", "", "1200" }) == ColumnDataType.Integer);
        }

        private static void Pool()
        {
            Program.Check("slot names are padded", ParameterPool.SlotName(ColumnDataType.Text, 3) == "PAC_T03");
            Program.Check("each type has its own letter", ParameterPool.SlotName(ColumnDataType.Area, 1) == "PAC_A01");
            Program.Check("pac parameters are recognised", ParameterPool.IsPacParameter("PAC_T01"));
            Program.Check("other parameters are not", !ParameterPool.IsPacParameter("Comments"));

            var limits = ImportLimits.Default;
            Program.Check("the pool is a fixed size however many sheets are imported",
                ParameterPool.AllParameterNames(limits).Count() == 4 + 24 + 8 + 4 + 4 + 2 + 2);
            Program.Check("the bookkeeping parameters are in it",
                ParameterPool.AllParameterNames(limits).Contains(ParameterPool.RowHashParameter));

            bool ok = ParameterPool.TryAllocate(
                new[] { ColumnDataType.Text, ColumnDataType.Text, ColumnDataType.Integer },
                limits, out IReadOnlyList<string> slots, out _);
            Program.Check("slots are handed out in order",
                ok && slots.SequenceEqual(new[] { "PAC_T01", "PAC_T02", "PAC_I01" }));

            bool over = ParameterPool.TryAllocate(
                Enumerable.Repeat(ColumnDataType.Text, 25), limits, out _, out string problem);
            Program.Check("running out of slots is refused", !over);
            Program.Check("and the refusal names the limit", problem.Contains("provides 24"));
        }

        private static void Mapping()
        {
            Program.Check("column letters count up", MappedColumn.Letter(0) == "A" && MappedColumn.Letter(25) == "Z");
            Program.Check("and carry over", MappedColumn.Letter(26) == "AA" && MappedColumn.Letter(27) == "AB");

            ColumnMap map = ColumnMap.Propose(Doors());
            Program.Check("every column is proposed", map.Columns.Count == 4);
            Program.Check("all are included by default", map.Included.Count == 4);
            Program.Check("types are guessed", map.ByHeading("Width").DataType == ColumnDataType.Integer);
            Program.Check("yes/no is guessed", map.ByHeading("Fire rated").DataType == ColumnDataType.YesNo);
            Program.Check("slots are assigned", map.ByHeading("Door ref").ParameterName == "PAC_T01");
            Program.Check("each type gets its own slot", map.ByHeading("Width").ParameterName == "PAC_I01");
            Program.Check("the display name defaults to the heading", map.ByHeading("Notes").DisplayName == "Notes");
            Program.Check("a unique column is suggested as the key",
                map.KeyColumn != null && map.KeyColumn.SourceHeading == "Door ref");
            Program.Check("the mapping describes itself", map.Describe().Contains("keyed on 'Door ref'"));
            Program.Check("a parameter can be looked up", map.ByParameter("PAC_T01").SourceHeading == "Door ref");

            WorksheetTable noHeading = new WorksheetTable("S", "A1:B2", new[] { "", "Width" },
                new[] { new[] { "x", "1" } }, 2);
            Program.Check("a blank heading gets a name",
                ColumnMap.Propose(noHeading).Columns[0].DisplayName == "Column A");

            ColumnMap excluded = ColumnMap.Propose(Doors());
            excluded.ByHeading("Notes").Included = false;
            excluded.ReassignSlots();
            Program.Check("an excluded column loses its slot", excluded.ByHeading("Notes").ParameterName == null);
            Program.Check("the others keep theirs", excluded.ByHeading("Door ref").ParameterName == "PAC_T01");

            ColumnMap keyed = ColumnMap.Propose(Doors());
            keyed.SetKey(keyed.ByHeading("Notes"));
            Program.Check("setting a key clears the old one", keyed.KeyColumn.SourceHeading == "Notes");
            keyed.SetKey(null);
            Program.Check("the key can be cleared", keyed.KeyColumn == null && keyed.MatchesByOrder);

            Program.Check("a clone is independent",
                ColumnMap.Propose(Doors()).Clone().Columns[0] != map.Columns[0]);
        }

        private static void Reconciling()
        {
            ColumnMap saved = ColumnMap.Propose(Doors());

            var unchanged = HeadingReconciler.Compare(saved, Doors());
            Program.Check("an unchanged sheet reports nothing", unchanged.Count == 0);

            WorksheetTable renamed = new WorksheetTable("Doors", "A3:D7",
                new[] { "Door ref", "Width", "Fire rated", "Remarks" },
                new[] { new[] { "D-101", "900", "Yes", "Entrance" } }, 4);
            var changes = HeadingReconciler.Compare(saved, renamed);
            Program.Check("a renamed heading is spotted",
                changes.Count == 1 && changes[0].Kind == HeadingChangeKind.Renamed);
            Program.Check("the rename names both headings",
                changes[0].OldHeading == "Notes" && changes[0].NewHeading == "Remarks");
            Program.Check("the rename asks rather than decides", changes[0].Message.Contains("Keep the mapping, or map it again?"));

            WorksheetTable moved = new WorksheetTable("Doors", "A3:D7",
                new[] { "Notes", "Door ref", "Width", "Fire rated" },
                new[] { new[] { "Entrance", "D-101", "900", "Yes" } }, 4);
            Program.Check("columns that merely moved are not reported as changes",
                HeadingReconciler.Compare(saved, moved).Count == 0);

            WorksheetTable dropped = new WorksheetTable("Doors", "A3:C7",
                new[] { "Door ref", "Width", "Fire rated" },
                new[] { new[] { "D-101", "900", "Yes" } }, 4);
            var removals = HeadingReconciler.Compare(saved, dropped);
            Program.Check("a removed column is spotted",
                removals.Count == 1 && removals[0].Kind == HeadingChangeKind.Removed);
            Program.Check("removal offers to keep the values", removals[0].Message.Contains("Keep the Revit column"));

            WorksheetTable added = new WorksheetTable("Doors", "A3:E7",
                new[] { "Door ref", "Width", "Fire rated", "Notes", "Hardware" },
                new[] { new[] { "D-101", "900", "Yes", "Entrance", "HW-1" } }, 4);
            var additions = HeadingReconciler.Compare(saved, added);
            Program.Check("a new column is spotted",
                additions.Count == 1 && additions[0].Kind == HeadingChangeKind.Added);
            Program.Check("a new column is never added silently", additions[0].Message.Contains("Add it to the schedule?"));
        }

        private static void Validating()
        {
            ColumnMap map = ColumnMap.Propose(Doors());
            ValidationReport clean = ImportValidator.Validate(Doors(), map);
            Program.Check("a good sheet validates", !clean.HasErrors);

            Program.Check("an empty range is an error",
                ImportValidator.Validate(new WorksheetTable("S", "A1", new string[0], null, 1), map)
                               .Has(ImportValidator.Codes.EmptyRange));

            ColumnMap nothing = ColumnMap.Propose(Doors());
            foreach (MappedColumn c in nothing.Columns) c.Included = false;
            Program.Check("no columns is an error",
                ImportValidator.Validate(Doors(), nothing).Has(ImportValidator.Codes.NoColumns));

            WorksheetTable dup = new WorksheetTable("S", "A1:B3", new[] { "Ref", "Ref" },
                new[] { new[] { "a", "b" }, new[] { "c", "d" } }, 2);
            Program.Check("a duplicated heading is a warning",
                ImportValidator.Validate(dup, ColumnMap.Propose(dup)).Has(ImportValidator.Codes.DuplicateHeading));

            WorksheetTable dupKeys = new WorksheetTable("S", "A1:B4", new[] { "Ref", "Note" },
                new[] { new[] { "D-1", "x" }, new[] { "D-1", "y" } }, 2);
            ColumnMap keyMap = ColumnMap.Propose(dupKeys);
            keyMap.SetKey(keyMap.ByHeading("Ref"));
            ValidationReport dupReport = ImportValidator.Validate(dupKeys, keyMap);
            Program.Check("a duplicated key is an error", dupReport.Has(ImportValidator.Codes.DuplicateKey));
            Program.Check("and it names both rows",
                dupReport.Errors.Any(i => i.Message.Contains("rows 2 and 3")));

            WorksheetTable blankKey = new WorksheetTable("S", "A1:B3", new[] { "Ref", "Note" },
                new[] { new[] { "", "x" } }, 2);
            ColumnMap blankMap = ColumnMap.Propose(blankKey);
            blankMap.SetKey(blankMap.Columns[0]);
            Program.Check("an empty key is an error",
                ImportValidator.Validate(blankKey, blankMap).Has(ImportValidator.Codes.BlankKey));

            ColumnMap noKey = ColumnMap.Propose(Doors());
            noKey.SetKey(null);
            ValidationReport orderReport = ImportValidator.Validate(Doors(), noKey);
            Program.Check("no key column is warned about", orderReport.Has(ImportValidator.Codes.NoKeyColumn));
            Program.Check("and the warning explains the risk",
                orderReport.Warnings.Any(i => i.Message.Contains("re-label every row below it")));
            Program.Check("no key is not fatal by default", !orderReport.HasErrors);

            var strict = ImportLimits.Default;
            strict.RequireKeyColumn = true;
            Program.Check("but it can be made fatal",
                ImportValidator.Validate(Doors(), noKey, strict).HasErrors);

            WorksheetTable bad = new WorksheetTable("S", "A1:B4", new[] { "Ref", "Width" },
                new[] { new[] { "D-1", "900" }, new[] { "D-2", "wide" } }, 2);
            ColumnMap badMap = ColumnMap.Propose(bad);
            badMap.ByHeading("Width").DataType = ColumnDataType.Integer;
            ValidationReport badReport = ImportValidator.Validate(bad, badMap);
            Program.Check("a cell that will not parse is a warning", badReport.Has(ImportValidator.Codes.BadCell));
            Program.Check("a bad cell does not stop the import", !badReport.HasErrors);
            Program.Check("the bad cell is located",
                badReport.Warnings.Any(i => i.SpreadsheetRow == 3 && i.Where.Contains("column B")));
            Program.Check("and says the cell is left empty",
                badReport.Warnings.Any(i => i.Message.Contains("left empty")));

            var tiny = ImportLimits.Default;
            tiny.RowWarningThreshold = 2;
            Program.Check("a big sheet is warned about",
                ImportValidator.Validate(Doors(), map, tiny).Has(ImportValidator.Codes.ManyRows));
            tiny.RowHardLimit = 2;
            Program.Check("a far too big sheet is refused",
                ImportValidator.Validate(Doors(), map, tiny).Has(ImportValidator.Codes.TooManyRows));

            WorksheetTable withBlank = new WorksheetTable("S", "A1:B4", new[] { "Ref", "Note" },
                new[] { new[] { "D-1", "x" }, new[] { "", "" } }, 2);
            Program.Check("a blank row is noted, not imported",
                ImportValidator.Validate(withBlank, ColumnMap.Propose(withBlank)).Has(ImportValidator.Codes.BlankRow));

            Program.Check("the summary counts the warnings",
                ImportValidator.Validate(bad, badMap).Summary.Contains("warning"));
            Program.Check("a clean sheet has nothing to report", clean.IsClean && clean.Summary == "Nothing to report.");
        }

        private static void Diffing()
        {
            WorksheetTable table = Doors();
            ColumnMap map = ColumnMap.Propose(table);
            IReadOnlyList<SourceRow> rows = SourceRows.Build(table, map);

            Program.Check("rows are built", rows.Count == 3);
            Program.Check("keys come from the key column", rows[0].Key == "D-101");
            Program.Check("rows know their spreadsheet row", rows[1].SpreadsheetRow == 5);
            Program.Check("values are keyed by parameter", rows[0].Values["PAC_T01"] == "D-101");

            ColumnMap byOrder = ColumnMap.Propose(table);
            byOrder.SetKey(null);
            Program.Check("without a key column rows are keyed by position",
                SourceRows.Build(table, byOrder)[1].Key == "#2");

            // Nothing in the model yet: every row is an addition.
            DiffResult fresh = RowDiffer.Diff(new List<CarrierRow>(), table, map);
            Program.Check("an empty model means everything is added",
                fresh.Added.Count == 3 && fresh.Changed.Count == 0 && fresh.Removed.Count == 0);
            Program.Check("a diff with changes says so", fresh.AnyChange);

            // The model matches the spreadsheet exactly.
            List<CarrierRow> same = rows
                .Select(r => new CarrierRow("e" + r.Order, r.Key, r.Order, r.Hash, r.Values))
                .ToList();
            DiffResult nothing = RowDiffer.Diff(same, table, map);
            Program.Check("a matching model means no changes", !nothing.AnyChange && nothing.Unchanged.Count == 3);
            Program.Check("and it says there is nothing to do", nothing.Summary.Contains("nothing to do"));

            // One row edited in the spreadsheet, one row gone, one row new.
            WorksheetTable edited = new WorksheetTable("Doors", "A3:D7",
                new[] { "Door ref", "Width", "Fire rated", "Notes" },
                new[]
                {
                    new[] { "D-101", "950",   "Yes", "Entrance" },
                    new[] { "D-103", "900",   "Yes", "Lobby" },
                    new[] { "D-104", "1,000", "No",  "Store" }
                }, 4);

            DiffResult diff = RowDiffer.Diff(same, edited, map);
            Program.Check("an edited row is a change", diff.Changed.Count == 1 && diff.Changed[0].Key == "D-101");
            Program.Check("a new row is an addition", diff.Added.Count == 1 && diff.Added[0].Key == "D-104");
            Program.Check("a vanished row is a removal", diff.Removed.Count == 1 && diff.Removed[0].Key == "D-102");
            Program.Check("an untouched row is left alone", diff.Unchanged.Count == 1);
            Program.Check("the change names the column that differs",
                diff.Changed[0].ChangedColumns.Contains("Width"));
            Program.Check("the summary reads as a question to answer",
                diff.Summary.Contains("1 changed") && diff.Summary.Contains("no longer in the spreadsheet"));
            Program.Check("a removal is described, not just counted",
                diff.Removed[0].Describe().Contains("no longer in the spreadsheet"));

            WorksheetTable withBlank = new WorksheetTable("S", "A1:B4", new[] { "Ref", "N" },
                new[] { new[] { "D-1", "x" }, new[] { "", "" }, new[] { "D-2", "y" } }, 2);
            IReadOnlyList<SourceRow> skipped = SourceRows.Build(withBlank, ColumnMap.Propose(withBlank));
            Program.Check("a blank row is not imported", skipped.Count == 2);
            Program.Check("and the rows after it keep counting up", skipped[1].Order == 2);
            Program.Check("while their spreadsheet row number is still right", skipped[1].SpreadsheetRow == 4);

            var applied = new ApplyResult { Added = 2, Updated = 1 };
            Program.Check("an apply result reads back", applied.AnyChange && applied.Summary.Contains("1 updated"));
            Program.Check("and it mentions undo", applied.Summary.Contains("Ctrl+Z"));
            applied.Skipped.Add("'D-9' is borrowed by Ravi");
            Program.Check("skipped rows are reported with the reason", applied.Summary.Contains("borrowed by Ravi"));
            Program.Check("no change means no change", !new ApplyResult().AnyChange);
        }

        private static void Records()
        {
            ColumnMap map = ColumnMap.Propose(Doors());
            var record = new LinkRecord
            {
                WorkbookPathAbsolute = @"C:\Jobs\Metro\Doors.xlsx",
                WorkbookPathRelative = @"..\Doors.xlsx",
                WorksheetName = "Doors",
                RangeRef = "A3:D7",
                FirstDataRowNumber = 4,
                ContentHash = Doors().ContentHash(),
                SourceLastWriteUtc = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc),
                LastRefreshUtc = new DateTime(2026, 11, 1, 10, 5, 0, DateTimeKind.Utc),
                LastRefreshBy = "Vineet",
                ScheduleRef = "12345",
                ScheduleName = "Door schedule",
                RowCount = 3
            };
            record.SetColumnMap(map);

            LinkRecord back = LinkRecord.FromJson(record.ToJson());
            Program.Check("a record survives a round trip", back != null && back.DataSetId == record.DataSetId);
            Program.Check("the mapping survives", back.Columns.Count == 4);
            Program.Check("types survive as names, not numbers",
                record.ToJson().Contains("YesNo") && back.ToColumnMap().ByHeading("Fire rated").DataType == ColumnDataType.YesNo);
            Program.Check("the key survives", back.ToColumnMap().KeyColumn.SourceHeading == "Door ref");
            Program.Check("the selection can be rebuilt", back.ToSelection().A1Range == "A3:D7");
            Program.Check("no row data is stored", !record.ToJson().Contains("D-101"));

            Program.Check("corrupt json is not a crash", LinkRecord.FromJson("{ not json") == null);
            Program.Check("empty json is not a crash", LinkRecord.FromJson("") == null);

            Program.Check("a newer record is spotted", new LinkRecord { SchemaVersion = 99 }.IsFromNewerBuild);
            Program.Check("and explained", LinkRecord.NewerBuildMessage.Contains("newer version"));

            Program.Check("an unchanged file is ok",
                record.StateFrom(true, null, record.ContentHash, true) == LinkState.Ok);
            Program.Check("a changed file is spotted",
                record.StateFrom(true, null, "DIFFERENT", true) == LinkState.FileChanged);
            Program.Check("a missing file is spotted",
                record.StateFrom(false, null, null, true) == LinkState.FileMissing);
            Program.Check("a deleted schedule is spotted",
                record.StateFrom(true, null, record.ContentHash, false) == LinkState.ScheduleMissing);
            Program.Check("a newer write time is spotted without a hash",
                record.StateFrom(true, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), null, true) == LinkState.FileChanged);

            Program.Check("a missing file is explained with its path",
                LinkRecord.Explain(LinkState.FileMissing, @"C:\Jobs\Doors.xlsx").Contains(@"C:\Jobs\Doors.xlsx"));
            Program.Check("the record describes itself", record.Describe().Contains("Doors.xlsx") && record.Describe().Contains("3 rows"));
        }
    }
}
