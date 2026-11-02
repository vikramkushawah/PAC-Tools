// PAC Tools - everything that can be wrong with an import, found before the model is touched.
//
// The rule from CONTRIBUTING.md: validate first, then act. A wizard that lists twelve bad cells on screen
// is worth far more than a transaction that half-succeeds. Nothing in this file changes anything; it only
// reports, and every message names the thing that is wrong and what to do about it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PacTools.Core
{
    /// <summary>How much a validation issue matters.</summary>
    public enum IssueSeverity
    {
        /// <summary>Worth knowing. The import proceeds.</summary>
        Info = 0,

        /// <summary>The import proceeds, but something was skipped or assumed.</summary>
        Warning,

        /// <summary>The import cannot proceed until the user changes something.</summary>
        Error
    }

    /// <summary>One thing that is wrong, or worth saying, about an import.</summary>
    public sealed class ValidationIssue
    {
        /// <summary>Creates the issue.</summary>
        public ValidationIssue(IssueSeverity severity, string code, string message,
                               int? spreadsheetRow = null, int? columnIndex = null)
        {
            Severity = severity;
            Code = code;
            Message = message;
            SpreadsheetRow = spreadsheetRow;
            ColumnIndex = columnIndex;
        }

        /// <summary>How much it matters.</summary>
        public IssueSeverity Severity { get; }

        /// <summary>A short stable code, so the UI and the checks can refer to a case without matching prose.</summary>
        public string Code { get; }

        /// <summary>What the user is told.</summary>
        public string Message { get; }

        /// <summary>The spreadsheet row it is about, as Excel numbers rows. Null when it is not about a row.</summary>
        public int? SpreadsheetRow { get; }

        /// <summary>The spreadsheet column it is about, counting from 0. Null when it is not about a column.</summary>
        public int? ColumnIndex { get; }

        /// <summary>"Row 42, column D", "Column D", "Row 42", or empty.</summary>
        public string Where
        {
            get
            {
                string row = SpreadsheetRow.HasValue ? "Row " + SpreadsheetRow.Value.ToString(CultureInfo.CurrentCulture) : null;
                string col = ColumnIndex.HasValue ? "column " + MappedColumn.Letter(ColumnIndex.Value) : null;
                if (row != null && col != null) return row + ", " + col;
                if (row != null) return row;
                if (col != null) return char.ToUpperInvariant(col[0]) + col.Substring(1);
                return string.Empty;
            }
        }

        /// <inheritdoc />
        public override string ToString() =>
            Severity.ToString().ToUpperInvariant() + " " + Code + ": " + (Where.Length > 0 ? Where + " - " : "") + Message;
    }

    /// <summary>Everything found wrong with one import, and whether it can go ahead.</summary>
    public sealed class ValidationReport
    {
        private readonly List<ValidationIssue> _issues;

        /// <summary>Creates the report.</summary>
        public ValidationReport(IEnumerable<ValidationIssue> issues)
        {
            _issues = (issues ?? Enumerable.Empty<ValidationIssue>()).ToList();
        }

        /// <summary>Every issue, worst first, then in the order they were found.</summary>
        public IReadOnlyList<ValidationIssue> Issues =>
            _issues.OrderByDescending(i => i.Severity).ToList();

        /// <summary>Just the errors.</summary>
        public IReadOnlyList<ValidationIssue> Errors =>
            _issues.Where(i => i.Severity == IssueSeverity.Error).ToList();

        /// <summary>Just the warnings.</summary>
        public IReadOnlyList<ValidationIssue> Warnings =>
            _issues.Where(i => i.Severity == IssueSeverity.Warning).ToList();

        /// <summary>True when the import must not go ahead.</summary>
        public bool HasErrors => _issues.Any(i => i.Severity == IssueSeverity.Error);

        /// <summary>True when there is nothing at all to say.</summary>
        public bool IsClean => _issues.Count == 0;

        /// <summary>True when an issue with this code was found.</summary>
        public bool Has(string code) => _issues.Any(i => string.Equals(i.Code, code, StringComparison.Ordinal));

        /// <summary>"2 problems and 5 warnings." - the line above the Create button.</summary>
        public string Summary
        {
            get
            {
                int e = Errors.Count, w = Warnings.Count;
                if (e == 0 && w == 0) return "Nothing to report.";
                var parts = new List<string>();
                if (e > 0) parts.Add(e == 1 ? "1 problem" : e + " problems");
                if (w > 0) parts.Add(w == 1 ? "1 warning" : w + " warnings");
                return string.Join(" and ", parts) + ".";
            }
        }
    }

    /// <summary>Checks a table and its mapping before anything is written to Revit.</summary>
    public static class ImportValidator
    {
        /// <summary>Codes, so the UI and the off-screen checks never match on prose.</summary>
        public static class Codes
        {
            /// <summary>The range holds no rows or no columns.</summary>
            public const string EmptyRange = "EMPTY_RANGE";
            /// <summary>Every column is ticked off.</summary>
            public const string NoColumns = "NO_COLUMNS";
            /// <summary>A column has no heading in the spreadsheet.</summary>
            public const string BlankHeading = "BLANK_HEADING";
            /// <summary>Two columns share a heading.</summary>
            public const string DuplicateHeading = "DUPLICATE_HEADING";
            /// <summary>Two schedule columns would be called the same thing.</summary>
            public const string DuplicateDisplayName = "DUPLICATE_DISPLAY_NAME";
            /// <summary>More columns of one type than the parameter pool provides.</summary>
            public const string TooManyColumns = "TOO_MANY_COLUMNS";
            /// <summary>More rows than the model should carry.</summary>
            public const string TooManyRows = "TOO_MANY_ROWS";
            /// <summary>Enough rows to slow the model down.</summary>
            public const string ManyRows = "MANY_ROWS";
            /// <summary>No key column, so rows are matched by position.</summary>
            public const string NoKeyColumn = "NO_KEY_COLUMN";
            /// <summary>The key column is empty on some rows.</summary>
            public const string BlankKey = "BLANK_KEY";
            /// <summary>The same key appears on more than one row.</summary>
            public const string DuplicateKey = "DUPLICATE_KEY";
            /// <summary>A cell does not fit the type chosen for its column.</summary>
            public const string BadCell = "BAD_CELL";
            /// <summary>More bad cells than are listed one by one.</summary>
            public const string MoreBadCells = "MORE_BAD_CELLS";
            /// <summary>A row is entirely empty.</summary>
            public const string BlankRow = "BLANK_ROW";
        }

        /// <summary>Checks everything, in the order the user would meet it.</summary>
        public static ValidationReport Validate(WorksheetTable table, ColumnMap map, ImportLimits limits = null)
        {
            limits = limits ?? ImportLimits.Default;
            var issues = new List<ValidationIssue>();

            if (table == null || table.IsEmpty)
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error, Codes.EmptyRange,
                    "The range is empty - there is nothing to import. Check the worksheet and the range."));
                return new ValidationReport(issues);
            }

            if (map == null || map.Included.Count == 0)
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error, Codes.NoColumns,
                    "No columns are ticked. Choose at least one column to import."));
                return new ValidationReport(issues);
            }

            CheckHeadings(table, map, issues);
            CheckCapacity(map, limits, issues);
            CheckRowCount(table, limits, issues);
            CheckKey(table, map, limits, issues);
            CheckCells(table, map, limits, issues);

            return new ValidationReport(issues);
        }

        private static void CheckHeadings(WorksheetTable table, ColumnMap map, List<ValidationIssue> issues)
        {
            foreach (MappedColumn c in map.Included.Where(c => string.IsNullOrWhiteSpace(c.SourceHeading)))
                issues.Add(new ValidationIssue(IssueSeverity.Warning, Codes.BlankHeading,
                    "Column " + c.ColumnLetter + " has no heading. It will be called '" + c.DisplayName +
                    "'. Type a heading in the spreadsheet, or rename the column here.", null, c.SourceIndex));

            foreach (var group in map.Included
                         .Where(c => !string.IsNullOrWhiteSpace(c.SourceHeading))
                         .GroupBy(c => c.SourceHeading.Trim(), StringComparer.OrdinalIgnoreCase)
                         .Where(g => g.Count() > 1))
            {
                issues.Add(new ValidationIssue(IssueSeverity.Warning, Codes.DuplicateHeading,
                    "The heading '" + group.Key + "' is used by columns " +
                    string.Join(" and ", group.Select(c => c.ColumnLetter)) +
                    ". They will be told apart by their position, so a refresh may map them the wrong way " +
                    "round if the columns move. Give them different headings."));
            }

            foreach (var group in map.Included
                         .GroupBy(c => (c.DisplayName ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                         .Where(g => g.Count() > 1))
            {
                issues.Add(new ValidationIssue(IssueSeverity.Warning, Codes.DuplicateDisplayName,
                    "Two schedule columns would both be called '" + group.Key +
                    "'. Rename one so the schedule can be read."));
            }
        }

        private static void CheckCapacity(ColumnMap map, ImportLimits limits, List<ValidationIssue> issues)
        {
            foreach (var group in map.Included.GroupBy(c => c.DataType))
            {
                int capacity = limits.CapacityOf(group.Key);
                int wanted = group.Count();
                if (wanted <= capacity) continue;

                issues.Add(new ValidationIssue(IssueSeverity.Error, Codes.TooManyColumns,
                    "This selection has " + wanted + " " + ValueParser.Label(group.Key).ToLowerInvariant() +
                    " column(s); PAC Tools provides " + capacity + ". Tick some off, or change a column's type."));
            }

            // A column that lost its slot during mapping has no parameter to write to.
            foreach (MappedColumn c in map.Included.Where(c => string.IsNullOrEmpty(c.ParameterName)))
                issues.Add(new ValidationIssue(IssueSeverity.Error, Codes.TooManyColumns,
                    "'" + c.DisplayName + "' has nowhere to be stored. Tick it off, or free up a column of its type.",
                    null, c.SourceIndex));
        }

        private static void CheckRowCount(WorksheetTable table, ImportLimits limits, List<ValidationIssue> issues)
        {
            if (table.RowCount > limits.RowHardLimit)
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error, Codes.TooManyRows,
                    "The range has " + table.RowCount.ToString("N0", CultureInfo.CurrentCulture) +
                    " rows. PAC Tools imports at most " + limits.RowHardLimit.ToString("N0", CultureInfo.CurrentCulture) +
                    "; beyond that the model becomes unusable. Import a smaller range."));
            }
            else if (table.RowCount > limits.RowWarningThreshold)
            {
                issues.Add(new ValidationIssue(IssueSeverity.Warning, Codes.ManyRows,
                    "This will add " + table.RowCount.ToString("N0", CultureInfo.CurrentCulture) +
                    " elements to the model, which will slow it down. Consider importing a range instead."));
            }

            int blank = 0;
            for (int r = 0; r < table.RowCount; r++)
                if (table.Rows[r].All(string.IsNullOrWhiteSpace)) blank++;

            if (blank > 0)
                issues.Add(new ValidationIssue(IssueSeverity.Info, Codes.BlankRow,
                    blank == 1 ? "1 row is empty and will be skipped." : blank + " rows are empty and will be skipped."));
        }

        private static void CheckKey(WorksheetTable table, ColumnMap map, ImportLimits limits, List<ValidationIssue> issues)
        {
            MappedColumn key = map.KeyColumn;

            if (key == null)
            {
                issues.Add(new ValidationIssue(
                    limits.RequireKeyColumn ? IssueSeverity.Error : IssueSeverity.Warning,
                    Codes.NoKeyColumn,
                    "No column identifies a row. Rows will be matched by their position, so inserting a row in " +
                    "the spreadsheet will re-label every row below it on the next refresh. Choose a column whose " +
                    "values are unique - a reference or a mark."));
                return;
            }

            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int r = 0; r < table.RowCount; r++)
            {
                if (table.Rows[r].All(string.IsNullOrWhiteSpace)) continue;

                string value = (table.Cell(r, key.SourceIndex) ?? string.Empty).Trim();
                int rowNumber = table.SpreadsheetRowNumber(r);

                if (value.Length == 0)
                {
                    issues.Add(new ValidationIssue(IssueSeverity.Error, Codes.BlankKey,
                        "'" + key.DisplayName + "' is empty, but it is the column that identifies the row. " +
                        "Fill it in, or choose a different key column.", rowNumber, key.SourceIndex));
                    continue;
                }

                if (seen.TryGetValue(value, out int first))
                {
                    issues.Add(new ValidationIssue(IssueSeverity.Error, Codes.DuplicateKey,
                        "'" + value + "' appears in rows " + first + " and " + rowNumber + ". Values in '" +
                        key.DisplayName + "' must be unique, or choose a different key column.",
                        rowNumber, key.SourceIndex));
                }
                else
                {
                    seen[value] = rowNumber;
                }
            }
        }

        private static void CheckCells(WorksheetTable table, ColumnMap map, ImportLimits limits, List<ValidationIssue> issues)
        {
            int reported = 0, total = 0;

            foreach (MappedColumn column in map.Included.Where(c => c.DataType != ColumnDataType.Text))
            {
                for (int r = 0; r < table.RowCount; r++)
                {
                    if (table.Rows[r].All(string.IsNullOrWhiteSpace)) continue;

                    ParsedCell parsed = ValueParser.Parse(table.Cell(r, column.SourceIndex), column.DataType);
                    if (parsed.Ok) continue;

                    total++;
                    if (reported >= limits.MaxReportedCellProblems) continue;

                    reported++;
                    issues.Add(new ValidationIssue(IssueSeverity.Warning, Codes.BadCell,
                        parsed.Problem + " The cell will be left empty in '" + column.DisplayName + "'.",
                        table.SpreadsheetRowNumber(r), column.SourceIndex));
                }
            }

            if (total > reported)
                issues.Add(new ValidationIssue(IssueSeverity.Warning, Codes.MoreBadCells,
                    (total - reported) + " more cell(s) will not fit the type chosen for their column and will be " +
                    "left empty. Check the column types, or correct the spreadsheet."));
        }
    }
}
