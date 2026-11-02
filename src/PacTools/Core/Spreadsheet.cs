// PAC Tools - the spreadsheet as plain C#. No Revit API, no Excel library.
//
// Deliberate choice: every cell arrives as the TEXT the user sees in Excel. Typing happens later, per
// column, in ValueParser - driven by what the user chose in the mapping step. That keeps this layer free
// of any Excel type system and makes "this value will not fit that type" a reportable fact rather than a
// silent coercion.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PacTools.Core
{
    /// <summary>Workbook formats PAC Tools can be pointed at.</summary>
    public enum WorkbookFormat
    {
        /// <summary>Not a format PAC Tools reads.</summary>
        Unsupported = 0,

        /// <summary>Excel 2007 and later.</summary>
        Xlsx,

        /// <summary>Excel 2007 and later, macro-enabled.</summary>
        Xlsm,

        /// <summary>Excel 97-2003. Read-only, legacy.</summary>
        Xls,

        /// <summary>Comma or tab separated text.</summary>
        Csv
    }

    /// <summary>What formats are recognised, and how they are named to the user.</summary>
    public static class WorkbookFormats
    {
        /// <summary>Works out the format from the file extension alone (the file need not exist).</summary>
        public static WorkbookFormat FromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return WorkbookFormat.Unsupported;
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".xlsx": return WorkbookFormat.Xlsx;
                case ".xlsm": return WorkbookFormat.Xlsm;
                case ".xls": return WorkbookFormat.Xls;
                case ".csv":
                case ".txt":
                case ".tsv": return WorkbookFormat.Csv;
                default: return WorkbookFormat.Unsupported;
            }
        }

        /// <summary>True for formats that can be read but are not the preferred ones.</summary>
        public static bool IsLegacy(WorkbookFormat format) => format == WorkbookFormat.Xls;

        /// <summary>The message shown when a file cannot be read, naming what can.</summary>
        public static string UnsupportedMessage(string path)
        {
            string ext = string.IsNullOrWhiteSpace(path) ? "" : System.IO.Path.GetExtension(path);
            string what = string.IsNullOrEmpty(ext) ? "That file" : "A " + ext.TrimStart('.').ToUpperInvariant() + " file";
            string extra = ext.Equals(".xlsb", StringComparison.OrdinalIgnoreCase)
                ? " Open it in Excel and save it as .xlsx."
                : string.Empty;
            return what + " cannot be read. PAC Tools reads .xlsx, .xlsm, .xls and .csv." + extra;
        }
    }

    /// <summary>One worksheet in a workbook, as listed before anything is read.</summary>
    public sealed class WorksheetInfo
    {
        /// <summary>Creates the description.</summary>
        public WorksheetInfo(string name, int rows, int columns, bool hidden = false)
        {
            Name = name;
            Rows = rows;
            Columns = columns;
            IsHidden = hidden;
        }

        /// <summary>The worksheet's name, as Excel shows it on the tab.</summary>
        public string Name { get; }

        /// <summary>Rows in the used range.</summary>
        public int Rows { get; }

        /// <summary>Columns in the used range.</summary>
        public int Columns { get; }

        /// <summary>True when the sheet is hidden in Excel. Listed, but not offered first.</summary>
        public bool IsHidden { get; }

        /// <summary>"Doors (312 x 11)", for the picker.</summary>
        public string Label => Name + "  (" + Rows + " x " + Columns + ")" + (IsHidden ? "  - hidden" : string.Empty);
    }

    /// <summary>A named range or a named table in a workbook - the "selection" of requirement 3.</summary>
    public sealed class NamedRangeInfo
    {
        /// <summary>Creates the description.</summary>
        public NamedRangeInfo(string name, string worksheetName, string a1Reference, bool isTable)
        {
            Name = name;
            WorksheetName = worksheetName;
            A1Reference = a1Reference;
            IsTable = isTable;
        }

        /// <summary>The name as defined in Excel.</summary>
        public string Name { get; }

        /// <summary>The worksheet it sits on.</summary>
        public string WorksheetName { get; }

        /// <summary>Where it is, e.g. "B4:H120".</summary>
        public string A1Reference { get; }

        /// <summary>True for an Excel table (ListObject), false for a plain named range.</summary>
        public bool IsTable { get; }

        /// <summary>"Table: DoorSchedule - Doors!B4:H120".</summary>
        public string Label => (IsTable ? "Table: " : "Range: ") + Name + "  -  " + WorksheetName + "!" + A1Reference;
    }

    /// <summary>What a workbook contains, read without reading any data.</summary>
    public sealed class WorkbookInfo
    {
        /// <summary>Creates the description.</summary>
        public WorkbookInfo(string path, WorkbookFormat format,
                            IReadOnlyList<WorksheetInfo> worksheets, IReadOnlyList<NamedRangeInfo> namedRanges)
        {
            Path = path;
            Format = format;
            Worksheets = worksheets ?? new List<WorksheetInfo>();
            NamedRanges = namedRanges ?? new List<NamedRangeInfo>();
        }

        /// <summary>Where the workbook was read from.</summary>
        public string Path { get; }

        /// <summary>Which format it turned out to be.</summary>
        public WorkbookFormat Format { get; }

        /// <summary>Every worksheet, in workbook order.</summary>
        public IReadOnlyList<WorksheetInfo> Worksheets { get; }

        /// <summary>Every named table and named range.</summary>
        public IReadOnlyList<NamedRangeInfo> NamedRanges { get; }
    }

    /// <summary>Which part of a workbook to read.</summary>
    public sealed class SheetSelection
    {
        /// <summary>The worksheet to read. Ignored when <see cref="NamedRange"/> is set.</summary>
        public string WorksheetName { get; set; }

        /// <summary>An A1 range such as "B4:H120". Null means the sheet's whole used range.</summary>
        public string A1Range { get; set; }

        /// <summary>A named table or named range to read instead of a worksheet + range.</summary>
        public string NamedRange { get; set; }

        /// <summary>True when the first row of the range holds the column headings.</summary>
        public bool FirstRowIsHeadings { get; set; } = true;

        /// <summary>How the selection reads in a message.</summary>
        public string Describe()
        {
            if (!string.IsNullOrEmpty(NamedRange)) return "'" + NamedRange + "'";
            string where = "'" + (WorksheetName ?? "(no worksheet)") + "'";
            return string.IsNullOrEmpty(A1Range) ? where : where + "!" + A1Range;
        }

        /// <summary>A copy, so a wizard step can be changed without disturbing the previous one.</summary>
        public SheetSelection Clone() => new SheetSelection
        {
            WorksheetName = WorksheetName,
            A1Range = A1Range,
            NamedRange = NamedRange,
            FirstRowIsHeadings = FirstRowIsHeadings
        };
    }

    /// <summary>
    /// A rectangle of spreadsheet cells, as text, with its headings. This is the only shape of spreadsheet
    /// data the rest of PAC Tools knows about.
    /// </summary>
    public sealed class WorksheetTable
    {
        private readonly List<string> _headings;
        private readonly List<IReadOnlyList<string>> _rows;

        /// <summary>Creates the table. Rows shorter than the heading count are padded with empty cells.</summary>
        public WorksheetTable(string worksheetName, string rangeRef,
                              IEnumerable<string> headings, IEnumerable<IEnumerable<string>> rows,
                              int firstDataRowNumber)
        {
            WorksheetName = worksheetName;
            RangeRef = rangeRef;
            FirstDataRowNumber = firstDataRowNumber;

            _headings = (headings ?? Enumerable.Empty<string>()).Select(h => (h ?? string.Empty).Trim()).ToList();
            int width = _headings.Count;

            _rows = new List<IReadOnlyList<string>>();
            foreach (IEnumerable<string> row in rows ?? Enumerable.Empty<IEnumerable<string>>())
            {
                List<string> cells = (row ?? Enumerable.Empty<string>()).Select(c => c ?? string.Empty).ToList();
                while (cells.Count < width) cells.Add(string.Empty);
                if (cells.Count > width) cells = cells.Take(width).ToList();
                _rows.Add(cells);
            }
        }

        /// <summary>The worksheet the range came from.</summary>
        public string WorksheetName { get; }

        /// <summary>The range that was read, e.g. "B4:H120", or the named table's name.</summary>
        public string RangeRef { get; }

        /// <summary>The spreadsheet row number (1-based, as Excel shows it) of <c>Rows[0]</c>.</summary>
        public int FirstDataRowNumber { get; }

        /// <summary>The column headings, trimmed.</summary>
        public IReadOnlyList<string> Headings => _headings;

        /// <summary>The data rows, each exactly <see cref="ColumnCount"/> cells wide.</summary>
        public IReadOnlyList<IReadOnlyList<string>> Rows => _rows;

        /// <summary>How many columns the table has.</summary>
        public int ColumnCount => _headings.Count;

        /// <summary>How many data rows the table has (headings excluded).</summary>
        public int RowCount => _rows.Count;

        /// <summary>True when there is nothing to import.</summary>
        public bool IsEmpty => RowCount == 0 || ColumnCount == 0;

        /// <summary>The spreadsheet row number of a data row, for use in messages.</summary>
        public int SpreadsheetRowNumber(int dataRowIndex) => FirstDataRowNumber + dataRowIndex;

        /// <summary>One cell, or empty when the indexes are outside the table.</summary>
        public string Cell(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || rowIndex >= _rows.Count) return string.Empty;
            if (columnIndex < 0 || columnIndex >= ColumnCount) return string.Empty;
            return _rows[rowIndex][columnIndex];
        }

        /// <summary>Every value in one column, for guessing its type.</summary>
        public IEnumerable<string> Column(int columnIndex) =>
            _rows.Select(r => columnIndex >= 0 && columnIndex < r.Count ? r[columnIndex] : string.Empty);

        /// <summary>The first <paramref name="count"/> rows, for the preview grid.</summary>
        public IReadOnlyList<IReadOnlyList<string>> Preview(int count = 20) => _rows.Take(Math.Max(0, count)).ToList();

        /// <summary>
        /// A SHA-256 of the headings and every cell. Stored with the link so "has the spreadsheet changed
        /// since the last refresh?" is answerable without comparing the whole table.
        /// </summary>
        public string ContentHash()
        {
            var sb = new StringBuilder();
            sb.Append(ColumnCount.ToString(CultureInfo.InvariantCulture)).Append('\u001e');
            foreach (string h in _headings) sb.Append(h).Append('\u001f');
            sb.Append('\u001e');
            foreach (IReadOnlyList<string> row in _rows)
            {
                foreach (string c in row) sb.Append(c).Append('\u001f');
                sb.Append('\u001e');
            }

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return Convert.ToHexString(hash);
            }
        }
    }

    /// <summary>
    /// Reads workbooks. Implemented in <c>Excel/</c> (ClosedXML for .xlsx/.xlsm, ExcelDataReader for .xls,
    /// a built-in reader for .csv) and by a fake in the off-screen checks.
    /// </summary>
    public interface ISpreadsheetReader
    {
        /// <summary>Which formats this reader handles.</summary>
        bool CanRead(WorkbookFormat format);

        /// <summary>Lists the worksheets, named tables and named ranges without reading any data.</summary>
        WorkbookInfo ReadInfo(string path);

        /// <summary>
        /// Reads the selected rectangle as text. <paramref name="maxRows"/> caps the read so a wizard
        /// preview does not pull a 200,000-row sheet into memory; pass 0 for no cap.
        /// </summary>
        WorksheetTable Read(string path, SheetSelection selection, int maxRows = 0);
    }
}
