// PAC Tools - what a refresh would change, worked out before anything is changed.
//
// The carriers in the model ARE the previous state, so there is no stored snapshot to drift out of step
// with the model. A row is matched by its key (or by position when no key column was chosen) and compared
// by hash, never by displayed value - see ParameterPool.RowHashParameter for why that matters.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PacTools.Core
{
    /// <summary>What a refresh would do to one row.</summary>
    public enum RowChange
    {
        /// <summary>The spreadsheet and the model agree.</summary>
        Unchanged = 0,

        /// <summary>In the spreadsheet, not yet in the model.</summary>
        Added,

        /// <summary>In both, but the values differ.</summary>
        Changed,

        /// <summary>In the model, no longer in the spreadsheet.</summary>
        Removed
    }

    /// <summary>One row of the spreadsheet, reduced to what a refresh needs.</summary>
    public sealed class SourceRow
    {
        /// <summary>Creates the row.</summary>
        public SourceRow(int rowIndex, int spreadsheetRow, int order, string key, string hash,
                         IReadOnlyDictionary<string, string> values)
        {
            RowIndex = rowIndex;
            SpreadsheetRow = spreadsheetRow;
            Order = order;
            Key = key;
            Hash = hash;
            Values = values;
        }

        /// <summary>Where it sits in <see cref="WorksheetTable.Rows"/>.</summary>
        public int RowIndex { get; }

        /// <summary>Its row number as Excel shows it, for messages.</summary>
        public int SpreadsheetRow { get; }

        /// <summary>Its position among the non-blank rows, counting from 1.</summary>
        public int Order { get; }

        /// <summary>What identifies it: the key column's value, or "#12" when matching by position.</summary>
        public string Key { get; }

        /// <summary>A hash of its included cells.</summary>
        public string Hash { get; }

        /// <summary>The values to write, by pool parameter name.</summary>
        public IReadOnlyDictionary<string, string> Values { get; }
    }

    /// <summary>Turns a table plus a mapping into the rows a refresh works with.</summary>
    public static class SourceRows
    {
        /// <summary>The key given to a row when no key column was chosen.</summary>
        public static string OrderKey(int order) => "#" + order.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Builds the rows, skipping entirely blank ones. Blank rows are skipped rather than imported
        /// because a trailing blank row in a spreadsheet is an accident, not data.
        /// </summary>
        public static IReadOnlyList<SourceRow> Build(WorksheetTable table, ColumnMap map)
        {
            var rows = new List<SourceRow>();
            if (table == null || map == null) return rows;

            IReadOnlyList<MappedColumn> included = map.Included;
            MappedColumn key = map.KeyColumn;
            int order = 0;

            for (int r = 0; r < table.RowCount; r++)
            {
                if (table.Rows[r].All(string.IsNullOrWhiteSpace)) continue;
                order++;

                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                var forHash = new StringBuilder();

                foreach (MappedColumn column in included)
                {
                    string raw = (table.Cell(r, column.SourceIndex) ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(column.ParameterName)) values[column.ParameterName] = raw;
                    forHash.Append(raw).Append('\u001f');
                }

                string keyValue = key != null
                    ? (table.Cell(r, key.SourceIndex) ?? string.Empty).Trim()
                    : OrderKey(order);

                rows.Add(new SourceRow(r, table.SpreadsheetRowNumber(r), order, keyValue,
                                       Hash(forHash.ToString()), values));
            }

            return rows;
        }

        /// <summary>The hash stored with a row, so a refresh compares hashes rather than displayed values.</summary>
        public static string Hash(string joinedCells)
        {
            using (var sha = SHA256.Create())
                return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(joinedCells ?? string.Empty)));
        }
    }

    /// <summary>What a refresh would do to one row, with enough detail to show the user.</summary>
    public sealed class RowDelta
    {
        /// <summary>Creates the delta.</summary>
        public RowDelta(RowChange change, string key, SourceRow source, CarrierRow carrier,
                        IReadOnlyList<string> changedColumns = null)
        {
            Change = change;
            Key = key;
            Source = source;
            Carrier = carrier;
            ChangedColumns = changedColumns ?? new List<string>();
        }

        /// <summary>What would happen.</summary>
        public RowChange Change { get; }

        /// <summary>What identifies the row.</summary>
        public string Key { get; }

        /// <summary>The spreadsheet side. Null for <see cref="RowChange.Removed"/>.</summary>
        public SourceRow Source { get; }

        /// <summary>The model side. Null for <see cref="RowChange.Added"/>.</summary>
        public CarrierRow Carrier { get; }

        /// <summary>Which schedule columns differ. Only filled for <see cref="RowChange.Changed"/>.</summary>
        public IReadOnlyList<string> ChangedColumns { get; }

        /// <summary>The line shown in the confirm-before-apply list.</summary>
        public string Describe()
        {
            switch (Change)
            {
                case RowChange.Added:
                    return "Add '" + Key + "' (spreadsheet row " + Source.SpreadsheetRow + ")";
                case RowChange.Changed:
                    string which = ChangedColumns.Count == 0
                        ? string.Empty
                        : " - " + string.Join(", ", ChangedColumns);
                    return "Update '" + Key + "'" + which;
                case RowChange.Removed:
                    return "Remove '" + Key + "' - no longer in the spreadsheet";
                default:
                    return "'" + Key + "' is unchanged";
            }
        }
    }

    /// <summary>Everything a refresh would do.</summary>
    public sealed class DiffResult
    {
        private readonly List<RowDelta> _deltas;

        /// <summary>Creates the result.</summary>
        public DiffResult(IEnumerable<RowDelta> deltas)
        {
            _deltas = (deltas ?? Enumerable.Empty<RowDelta>()).ToList();
        }

        /// <summary>Every row, in the order it was compared.</summary>
        public IReadOnlyList<RowDelta> Deltas => _deltas;

        /// <summary>Rows to add.</summary>
        public IReadOnlyList<RowDelta> Added => Of(RowChange.Added);

        /// <summary>Rows to update.</summary>
        public IReadOnlyList<RowDelta> Changed => Of(RowChange.Changed);

        /// <summary>Rows no longer in the spreadsheet.</summary>
        public IReadOnlyList<RowDelta> Removed => Of(RowChange.Removed);

        /// <summary>Rows that match already.</summary>
        public IReadOnlyList<RowDelta> Unchanged => Of(RowChange.Unchanged);

        /// <summary>True when the refresh would change nothing - then it should not open a transaction.</summary>
        public bool AnyChange => Added.Count > 0 || Changed.Count > 0 || Removed.Count > 0;

        private IReadOnlyList<RowDelta> Of(RowChange change) => _deltas.Where(d => d.Change == change).ToList();

        /// <summary>
        /// "18 changed, 4 added, 2 no longer in the spreadsheet." - the question the user answers before
        /// anything is applied. Removals are worded, not just counted, because they destroy data.
        /// </summary>
        public string Summary
        {
            get
            {
                if (!AnyChange) return "The schedule already matches the spreadsheet - nothing to do.";

                var parts = new List<string>();
                if (Changed.Count > 0) parts.Add(Changed.Count + " changed");
                if (Added.Count > 0) parts.Add(Added.Count + " added");
                if (Removed.Count > 0)
                    parts.Add(Removed.Count + (Removed.Count == 1 ? " row is" : " rows are") + " no longer in the spreadsheet");

                return string.Join(", ", parts) + ".";
            }
        }
    }

    /// <summary>Compares the spreadsheet with the carriers already in the model.</summary>
    public static class RowDiffer
    {
        /// <summary>
        /// Works out what a refresh would do. Nothing is changed here, and removals are reported rather than
        /// assumed: deleting rows is the one thing a refresh does that cannot be undone by refreshing again.
        /// </summary>
        public static DiffResult Diff(IReadOnlyList<CarrierRow> carriers, WorksheetTable fresh, ColumnMap map)
        {
            var deltas = new List<RowDelta>();
            IReadOnlyList<SourceRow> sourceRows = SourceRows.Build(fresh, map);
            carriers = carriers ?? new List<CarrierRow>();

            // Last one wins if the model somehow holds two carriers with the same key; the duplicate then
            // shows up as Removed, which is what should happen to it.
            var byKey = new Dictionary<string, CarrierRow>(StringComparer.OrdinalIgnoreCase);
            foreach (CarrierRow carrier in carriers)
                byKey[carrier.Key ?? string.Empty] = carrier;

            var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (SourceRow row in sourceRows)
            {
                if (!byKey.TryGetValue(row.Key ?? string.Empty, out CarrierRow carrier))
                {
                    deltas.Add(new RowDelta(RowChange.Added, row.Key, row, null));
                    continue;
                }

                matched.Add(row.Key ?? string.Empty);

                if (string.Equals(carrier.Hash, row.Hash, StringComparison.Ordinal))
                {
                    deltas.Add(new RowDelta(RowChange.Unchanged, row.Key, row, carrier));
                    continue;
                }

                deltas.Add(new RowDelta(RowChange.Changed, row.Key, row, carrier, ChangedColumns(row, carrier, map)));
            }

            foreach (CarrierRow carrier in carriers.Where(c => !matched.Contains(c.Key ?? string.Empty)))
                deltas.Add(new RowDelta(RowChange.Removed, carrier.Key, null, carrier));

            return new DiffResult(deltas);
        }

        /// <summary>
        /// Which schedule columns differ, for the report. The hash already said the row changed; this is
        /// only to tell the user where, so a value that cannot be read back is simply not listed.
        /// </summary>
        private static IReadOnlyList<string> ChangedColumns(SourceRow row, CarrierRow carrier, ColumnMap map)
        {
            var names = new List<string>();

            foreach (MappedColumn column in map.Included.Where(c => !string.IsNullOrEmpty(c.ParameterName)))
            {
                row.Values.TryGetValue(column.ParameterName, out string now);
                carrier.Values.TryGetValue(column.ParameterName, out string before);

                if (!string.Equals((now ?? string.Empty).Trim(), (before ?? string.Empty).Trim(),
                                   StringComparison.Ordinal))
                    names.Add(column.DisplayName);
            }

            return names;
        }
    }
}
