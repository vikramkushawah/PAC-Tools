// PAC Tools - which spreadsheet column becomes which Revit column.
//
// The mapping is the contract between the spreadsheet and the model, and it is what a refresh is checked
// against. Note what it stores: the pool parameter (PAC_T03) AND the heading the user wants shown. Because
// the display name is schedule-level data and not a parameter name, a renamed spreadsheet heading is a
// one-string change here - no re-binding, no data migration. That is the point of the pool.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PacTools.Core
{
    /// <summary>One spreadsheet column, and what it becomes in Revit.</summary>
    public sealed class MappedColumn
    {
        /// <summary>Where the column sits in the spreadsheet range, counting from 0.</summary>
        public int SourceIndex { get; set; }

        /// <summary>The heading as it was last read from the spreadsheet.</summary>
        public string SourceHeading { get; set; }

        /// <summary>What the schedule column is called. Set on the schedule field, not on the parameter.</summary>
        public string DisplayName { get; set; }

        /// <summary>What the values are imported as.</summary>
        public ColumnDataType DataType { get; set; } = ColumnDataType.Text;

        /// <summary>The pool parameter the values are stored in, e.g. "PAC_T03". Null when not included.</summary>
        public string ParameterName { get; set; }

        /// <summary>Whether this column is imported at all.</summary>
        public bool Included { get; set; } = true;

        /// <summary>True for the column that identifies a row across refreshes. At most one.</summary>
        public bool IsKey { get; set; }

        /// <summary>The spreadsheet column letter, for messages: 0 becomes "A", 27 becomes "AB".</summary>
        public string ColumnLetter => Letter(SourceIndex);

        /// <summary>Turns a 0-based column index into Excel's letter.</summary>
        public static string Letter(int zeroBasedIndex)
        {
            if (zeroBasedIndex < 0) return "?";
            string s = string.Empty;
            int n = zeroBasedIndex;
            while (true)
            {
                s = (char)('A' + n % 26) + s;
                n = n / 26 - 1;
                if (n < 0) break;
            }
            return s;
        }

        /// <summary>A copy, so the wizard can edit without disturbing the saved mapping.</summary>
        public MappedColumn Clone() => new MappedColumn
        {
            SourceIndex = SourceIndex,
            SourceHeading = SourceHeading,
            DisplayName = DisplayName,
            DataType = DataType,
            ParameterName = ParameterName,
            Included = Included,
            IsKey = IsKey
        };

        /// <inheritdoc />
        public override string ToString() =>
            ColumnLetter + " '" + SourceHeading + "' -> " + (ParameterName ?? "(not imported)");
    }

    /// <summary>The whole mapping for one import.</summary>
    public sealed class ColumnMap
    {
        private readonly List<MappedColumn> _columns;

        /// <summary>Creates a mapping from existing columns.</summary>
        public ColumnMap(IEnumerable<MappedColumn> columns)
        {
            _columns = (columns ?? Enumerable.Empty<MappedColumn>()).ToList();
        }

        /// <summary>Every column, in spreadsheet order.</summary>
        public IReadOnlyList<MappedColumn> Columns => _columns;

        /// <summary>The columns actually being imported.</summary>
        public IReadOnlyList<MappedColumn> Included => _columns.Where(c => c.Included).ToList();

        /// <summary>The key column, or null when rows are matched by order instead.</summary>
        public MappedColumn KeyColumn => _columns.FirstOrDefault(c => c.IsKey && c.Included);

        /// <summary>True when rows will be matched by their position rather than by a value.</summary>
        public bool MatchesByOrder => KeyColumn == null;

        /// <summary>
        /// Proposes a mapping for a freshly read table: everything included, types guessed from the values,
        /// pool slots handed out in order, and a key column suggested where one is obvious.
        /// Columns that do not fit the pool come back with <see cref="MappedColumn.Included"/> false - the
        /// validator reports them rather than this silently dropping them.
        /// </summary>
        public static ColumnMap Propose(WorksheetTable table, ImportLimits limits = null)
        {
            limits = limits ?? ImportLimits.Default;
            var columns = new List<MappedColumn>();
            if (table == null) return new ColumnMap(columns);

            for (int i = 0; i < table.ColumnCount; i++)
            {
                string heading = table.Headings[i];
                columns.Add(new MappedColumn
                {
                    SourceIndex = i,
                    SourceHeading = heading,
                    DisplayName = string.IsNullOrWhiteSpace(heading) ? "Column " + MappedColumn.Letter(i) : heading,
                    DataType = TypeGuess.Guess(table.Column(i)),
                    Included = true
                });
            }

            var map = new ColumnMap(columns);
            map.ReassignSlots(limits);
            map.SuggestKey(table);
            return map;
        }

        /// <summary>
        /// Hands out pool parameters again, after the user changed a type or ticked a column off. Columns
        /// that no longer fit are excluded and named in <paramref name="overflow"/>.
        /// </summary>
        public void ReassignSlots(ImportLimits limits = null, IList<string> overflow = null)
        {
            limits = limits ?? ImportLimits.Default;
            var used = new Dictionary<ColumnDataType, int>();

            foreach (MappedColumn column in _columns)
            {
                if (!column.Included) { column.ParameterName = null; continue; }

                used.TryGetValue(column.DataType, out int already);
                int next = already + 1;

                if (next > limits.CapacityOf(column.DataType))
                {
                    column.ParameterName = null;
                    column.Included = false;
                    column.IsKey = false;
                    overflow?.Add(column.DisplayName);
                    continue;
                }

                used[column.DataType] = next;
                column.ParameterName = ParameterPool.SlotName(column.DataType, next);
            }
        }

        /// <summary>
        /// Marks a sensible key column if none is set: the first included text column whose values are all
        /// present and all different. Leaves the key unset when no column qualifies.
        /// </summary>
        public void SuggestKey(WorksheetTable table)
        {
            if (table == null || KeyColumn != null) return;

            foreach (MappedColumn column in _columns.Where(c => c.Included && c.DataType == ColumnDataType.Text))
            {
                List<string> values = table.Column(column.SourceIndex).Select(v => (v ?? string.Empty).Trim()).ToList();
                if (values.Count == 0) continue;
                if (values.Any(string.IsNullOrEmpty)) continue;
                if (values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count) continue;

                column.IsKey = true;
                return;
            }
        }

        /// <summary>Makes <paramref name="column"/> the key, clearing any other.</summary>
        public void SetKey(MappedColumn column)
        {
            foreach (MappedColumn c in _columns) c.IsKey = false;
            if (column != null && column.Included) column.IsKey = true;
        }

        /// <summary>The column for a heading, or null. Case-insensitive, like Excel.</summary>
        public MappedColumn ByHeading(string heading) =>
            _columns.FirstOrDefault(c => string.Equals(c.SourceHeading, heading, StringComparison.OrdinalIgnoreCase));

        /// <summary>The column stored in a given pool parameter, or null.</summary>
        public MappedColumn ByParameter(string parameterName) =>
            _columns.FirstOrDefault(c => string.Equals(c.ParameterName, parameterName, StringComparison.Ordinal));

        /// <summary>A deep copy.</summary>
        public ColumnMap Clone() => new ColumnMap(_columns.Select(c => c.Clone()));

        /// <summary>"11 of 14 columns, keyed on 'Door ref'".</summary>
        public string Describe()
        {
            int included = Included.Count;
            string keyed = KeyColumn == null
                ? "matched by row order"
                : "keyed on '" + KeyColumn.DisplayName + "'";
            return included + " of " + _columns.Count + " column(s), " + keyed;
        }
    }

    /// <summary>What happened to a spreadsheet heading since the last refresh.</summary>
    public enum HeadingChangeKind
    {
        /// <summary>The heading in that position now reads differently.</summary>
        Renamed,

        /// <summary>A heading the mapping does not know about.</summary>
        Added,

        /// <summary>A mapped heading is no longer in the spreadsheet.</summary>
        Removed
    }

    /// <summary>One difference between a saved mapping and the spreadsheet as it is now.</summary>
    public sealed class HeadingChange
    {
        /// <summary>Creates the change.</summary>
        public HeadingChange(HeadingChangeKind kind, int columnIndex, string oldHeading, string newHeading)
        {
            Kind = kind;
            ColumnIndex = columnIndex;
            OldHeading = oldHeading;
            NewHeading = newHeading;
        }

        /// <summary>What kind of change it is.</summary>
        public HeadingChangeKind Kind { get; }

        /// <summary>Which spreadsheet column, counting from 0.</summary>
        public int ColumnIndex { get; }

        /// <summary>The heading the mapping had. Null for <see cref="HeadingChangeKind.Added"/>.</summary>
        public string OldHeading { get; }

        /// <summary>The heading the spreadsheet has. Null for <see cref="HeadingChangeKind.Removed"/>.</summary>
        public string NewHeading { get; }

        /// <summary>The letter of the column involved.</summary>
        public string ColumnLetter => MappedColumn.Letter(ColumnIndex);

        /// <summary>The message the user is shown, which always offers a choice rather than deciding for them.</summary>
        public string Message
        {
            get
            {
                switch (Kind)
                {
                    case HeadingChangeKind.Renamed:
                        return "The heading in column " + ColumnLetter + " changed from '" + OldHeading +
                               "' to '" + NewHeading + "'. Keep the mapping, or map it again?";
                    case HeadingChangeKind.Added:
                        return "Column " + ColumnLetter + " ('" + NewHeading +
                               "') is new. Add it to the schedule?";
                    case HeadingChangeKind.Removed:
                        return "The column '" + OldHeading +
                               "' is no longer in the spreadsheet. Keep the Revit column and its values, or drop it?";
                    default:
                        return string.Empty;
                }
            }
        }
    }

    /// <summary>Compares a saved mapping with the spreadsheet as it is now.</summary>
    public static class HeadingReconciler
    {
        /// <summary>
        /// Works out what changed. Headings are matched by name first, so columns that merely moved are not
        /// reported; only then is a heading at a known position treated as renamed. Nothing is ever changed
        /// here - the caller decides, because only the user knows whether a rename means the same data.
        /// </summary>
        public static IReadOnlyList<HeadingChange> Compare(ColumnMap saved, WorksheetTable fresh)
        {
            var changes = new List<HeadingChange>();
            if (saved == null || fresh == null) return changes;

            var freshHeadings = fresh.Headings.ToList();
            var matchedFresh = new bool[freshHeadings.Count];
            var matchedSaved = new HashSet<MappedColumn>();

            // 1. Same name, wherever it now sits.
            foreach (MappedColumn column in saved.Columns)
            {
                int at = freshHeadings.FindIndex(h => string.Equals(h, column.SourceHeading, StringComparison.OrdinalIgnoreCase));
                if (at < 0 || matchedFresh[at]) continue;
                matchedFresh[at] = true;
                matchedSaved.Add(column);
            }

            // 2. Of what is left, a heading sitting where a mapped column used to be is a rename.
            foreach (MappedColumn column in saved.Columns.Where(c => !matchedSaved.Contains(c)))
            {
                int i = column.SourceIndex;
                if (i >= 0 && i < freshHeadings.Count && !matchedFresh[i])
                {
                    matchedFresh[i] = true;
                    matchedSaved.Add(column);
                    changes.Add(new HeadingChange(HeadingChangeKind.Renamed, i, column.SourceHeading, freshHeadings[i]));
                }
                else
                {
                    changes.Add(new HeadingChange(HeadingChangeKind.Removed, i, column.SourceHeading, null));
                }
            }

            // 3. Anything still unmatched in the spreadsheet is new.
            for (int i = 0; i < freshHeadings.Count; i++)
                if (!matchedFresh[i])
                    changes.Add(new HeadingChange(HeadingChangeKind.Added, i, null, freshHeadings[i]));

            return changes;
        }
    }
}
