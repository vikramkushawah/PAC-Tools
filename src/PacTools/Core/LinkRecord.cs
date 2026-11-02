// PAC Tools - the record that makes a schedule refreshable.
//
// It is small on purpose: the row data is NOT in here. The carriers in the model are the previous state, so
// this record holds only what is needed to find the spreadsheet again, read the same rectangle, map the
// columns the same way, and notice that the file has changed. It goes into Extensible Storage, which has
// no business holding a few thousand rows.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PacTools.Core
{
    /// <summary>One column, as stored with the link.</summary>
    public sealed class StoredColumn
    {
        /// <summary>Where the column sat in the range, counting from 0.</summary>
        public int SourceIndex { get; set; }

        /// <summary>The heading it had when it was last read.</summary>
        public string SourceHeading { get; set; }

        /// <summary>What the schedule column is called.</summary>
        public string DisplayName { get; set; }

        /// <summary>What it is imported as.</summary>
        public ColumnDataType DataType { get; set; }

        /// <summary>The pool parameter holding its values.</summary>
        public string ParameterName { get; set; }

        /// <summary>Whether it is imported.</summary>
        public bool Included { get; set; }

        /// <summary>Whether it identifies the row.</summary>
        public bool IsKey { get; set; }
    }

    /// <summary>How a link stands right now.</summary>
    public enum LinkState
    {
        /// <summary>The spreadsheet is where it was and has not changed.</summary>
        Ok = 0,

        /// <summary>The workbook is not at either remembered path.</summary>
        FileMissing,

        /// <summary>The workbook has been saved since the last refresh.</summary>
        FileChanged,

        /// <summary>The schedule this link fed has been deleted from the project.</summary>
        ScheduleMissing,

        /// <summary>Not checked yet.</summary>
        Unknown
    }

    /// <summary>Everything needed to refresh one imported schedule.</summary>
    public sealed class LinkRecord
    {
        /// <summary>The schema this build writes. A record with a higher number is refused politely.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>Which version of this record's layout was written.</summary>
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>Ties the link, its carriers and its schedule together.</summary>
        public Guid DataSetId { get; set; } = Guid.NewGuid();

        /// <summary>Where the workbook was when it was last read.</summary>
        public string WorkbookPathAbsolute { get; set; }

        /// <summary>
        /// The same workbook, relative to the folder holding the .rvt. Stored as well as the absolute path
        /// so that moving a whole project folder - the normal way a job gets archived - does not break the link.
        /// </summary>
        public string WorkbookPathRelative { get; set; }

        /// <summary>The worksheet that was read.</summary>
        public string WorksheetName { get; set; }

        /// <summary>The A1 range that was read, or null for the whole used range.</summary>
        public string RangeRef { get; set; }

        /// <summary>The named table or named range that was read, or null.</summary>
        public string NamedRange { get; set; }

        /// <summary>Whether the first row of the range was taken as headings.</summary>
        public bool FirstRowIsHeadings { get; set; } = true;

        /// <summary>The spreadsheet row number the data started on.</summary>
        public int FirstDataRowNumber { get; set; }

        /// <summary>A hash of the whole range as last read. Answers "has it changed?" exactly.</summary>
        public string ContentHash { get; set; }

        /// <summary>The workbook's last-write time when it was last read. A cheap check before hashing.</summary>
        public DateTime SourceLastWriteUtc { get; set; }

        /// <summary>When the link was last refreshed.</summary>
        public DateTime LastRefreshUtc { get; set; }

        /// <summary>Who refreshed it last. Useful in a workshared model.</summary>
        public string LastRefreshBy { get; set; }

        /// <summary>The schedule this link feeds, as the Revit layer refers to it.</summary>
        public string ScheduleRef { get; set; }

        /// <summary>The schedule's name, so the Manage links window can list it without opening the project browser.</summary>
        public string ScheduleName { get; set; }

        /// <summary>How many rows were imported last time.</summary>
        public int RowCount { get; set; }

        /// <summary>The column mapping.</summary>
        public List<StoredColumn> Columns { get; set; } = new List<StoredColumn>();

        /// <summary>The selection, rebuilt so the same rectangle can be read again.</summary>
        public SheetSelection ToSelection() => new SheetSelection
        {
            WorksheetName = WorksheetName,
            A1Range = RangeRef,
            NamedRange = NamedRange,
            FirstRowIsHeadings = FirstRowIsHeadings
        };

        /// <summary>The mapping, rebuilt.</summary>
        public ColumnMap ToColumnMap() => new ColumnMap(Columns.Select(c => new MappedColumn
        {
            SourceIndex = c.SourceIndex,
            SourceHeading = c.SourceHeading,
            DisplayName = c.DisplayName,
            DataType = c.DataType,
            ParameterName = c.ParameterName,
            Included = c.Included,
            IsKey = c.IsKey
        }));

        /// <summary>Copies a mapping into the record.</summary>
        public void SetColumnMap(ColumnMap map)
        {
            Columns = (map?.Columns ?? new List<MappedColumn>()).Select(c => new StoredColumn
            {
                SourceIndex = c.SourceIndex,
                SourceHeading = c.SourceHeading,
                DisplayName = c.DisplayName,
                DataType = c.DataType,
                ParameterName = c.ParameterName,
                Included = c.Included,
                IsKey = c.IsKey
            }).ToList();
        }

        /// <summary>What to show in the Manage links window.</summary>
        public LinkState StateFrom(bool fileExists, DateTime? lastWriteUtc, string freshContentHash, bool scheduleExists)
        {
            if (!scheduleExists) return LinkState.ScheduleMissing;
            if (!fileExists) return LinkState.FileMissing;

            if (!string.IsNullOrEmpty(freshContentHash))
                return string.Equals(freshContentHash, ContentHash, StringComparison.Ordinal)
                    ? LinkState.Ok
                    : LinkState.FileChanged;

            // No hash to hand: fall back to the cheap check.
            if (lastWriteUtc.HasValue && lastWriteUtc.Value > SourceLastWriteUtc) return LinkState.FileChanged;
            return LinkState.Ok;
        }

        /// <summary>What the state means, in a sentence the user can act on.</summary>
        public static string Explain(LinkState state, string workbookPath)
        {
            switch (state)
            {
                case LinkState.Ok:
                    return "Up to date.";
                case LinkState.FileChanged:
                    return "The spreadsheet has changed since the last refresh. Refresh to bring the changes in.";
                case LinkState.FileMissing:
                    return "The workbook is no longer at " + (workbookPath ?? "its remembered location") +
                           ". Locate it, or unlink the schedule.";
                case LinkState.ScheduleMissing:
                    return "The schedule has been deleted from this project. Re-create it, or delete the link " +
                           "and its data.";
                default:
                    return "Not checked yet.";
            }
        }

        /// <summary>"Doors.xlsx - 'Door schedule' - 312 rows - refreshed 01-11-2026 16:04".</summary>
        public string Describe()
        {
            string file = string.IsNullOrEmpty(WorkbookPathAbsolute)
                ? "(no workbook)"
                : System.IO.Path.GetFileName(WorkbookPathAbsolute);

            string what = !string.IsNullOrEmpty(NamedRange)
                ? "'" + NamedRange + "'"
                : "'" + (WorksheetName ?? "?") + "'" + (string.IsNullOrEmpty(RangeRef) ? "" : "!" + RangeRef);

            string when = LastRefreshUtc == default
                ? "never refreshed"
                : "refreshed " + LastRefreshUtc.ToLocalTime().ToString("dd-MM-yyyy HH:mm", CultureInfo.CurrentCulture);

            return file + " - " + what + " - " + RowCount + " rows - " + when;
        }

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>The record as JSON, which is what goes into Extensible Storage.</summary>
        public string ToJson() => JsonSerializer.Serialize(this, Json);

        /// <summary>
        /// Reads a record back. Returns null for anything that will not parse rather than throwing, because
        /// a corrupt record must not stop the rest of the project's links from loading.
        /// </summary>
        public static LinkRecord FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<LinkRecord>(json, Json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>True when this record was written by a newer build than the one reading it.</summary>
        public bool IsFromNewerBuild => SchemaVersion > CurrentSchemaVersion;

        /// <summary>What to tell the user about a record from a newer build.</summary>
        public static string NewerBuildMessage =>
            "This link was made with a newer version of PAC Tools and cannot be refreshed by this one. " +
            "Update PAC Tools, or delete the link and import the spreadsheet again.";
    }
}
