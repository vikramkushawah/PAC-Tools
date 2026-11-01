// PAC Tools - the Element Count sample tool's logic. Pure C#: no Revit API, no WPF.
//
// Everything that can be worked out without asking Revit belongs in a file like this one, because
// it can then be checked in seconds off-screen (tests/PacTools.Checks) instead of by hand in Revit.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PacTools.Core
{
    /// <summary>One row of the report: a category and how many elements are in it.</summary>
    public sealed class CategoryCount
    {
        /// <summary>Creates a row.</summary>
        public CategoryCount(string category, int count, int total)
        {
            Category = category;
            Count = count;
            Share = total > 0 ? (double)count / total : 0.0;
        }

        /// <summary>The category name, or "(no category)".</summary>
        public string Category { get; }

        /// <summary>How many elements are in this category.</summary>
        public int Count { get; }

        /// <summary>This category's share of the total, 0.0 to 1.0.</summary>
        public double Share { get; }

        /// <summary>The count with thousands separators, e.g. "1,204".</summary>
        public string CountText => Count.ToString("N0", CultureInfo.CurrentCulture);

        /// <summary>The share as a percentage, e.g. "12.4 %".</summary>
        public string ShareText => (Share * 100.0).ToString("0.0", CultureInfo.CurrentCulture) + " %";

        /// <inheritdoc />
        public override string ToString() => Category + " = " + Count;
    }

    /// <summary>
    /// How many elements there are per category. Built from the flat list of category names that
    /// <see cref="IModelService.ReadCategoryNames"/> returns.
    /// </summary>
    public sealed class CountReport
    {
        /// <summary>Shown instead of a category name when an element has no category.</summary>
        public const string NoCategory = "(no category)";

        private CountReport(IReadOnlyList<CategoryCount> rows, int total, CountScope scope)
        {
            Rows = rows;
            Total = total;
            Scope = scope;
        }

        /// <summary>The rows, biggest category first; ties are sorted by name.</summary>
        public IReadOnlyList<CategoryCount> Rows { get; }

        /// <summary>How many elements were counted in all.</summary>
        public int Total { get; }

        /// <summary>How many different categories were found.</summary>
        public int Categories => Rows.Count;

        /// <summary>What was counted.</summary>
        public CountScope Scope { get; }

        /// <summary>An empty report, for a window that has not read the model yet.</summary>
        public static CountReport Empty { get; } =
            new CountReport(new List<CategoryCount>(), 0, CountScope.ActiveView);

        /// <summary>
        /// Groups the category names and sorts them, biggest first. A null or blank name is counted
        /// as <see cref="NoCategory"/>. A null list gives an empty report rather than throwing.
        /// </summary>
        public static CountReport Build(IEnumerable<string> categoryNames, CountScope scope)
        {
            var counts = new Dictionary<string, int>(StringComparer.CurrentCulture);
            int total = 0;

            foreach (string raw in categoryNames ?? Enumerable.Empty<string>())
            {
                string name = string.IsNullOrWhiteSpace(raw) ? NoCategory : raw.Trim();
                counts.TryGetValue(name, out int sofar);
                counts[name] = sofar + 1;
                total++;
            }

            List<CategoryCount> rows = counts
                .OrderByDescending(p => p.Value)
                .ThenBy(p => p.Key, StringComparer.CurrentCulture)
                .Select(p => new CategoryCount(p.Key, p.Value, total))
                .ToList();

            return new CountReport(rows, total, scope);
        }

        /// <summary>A one-line summary, e.g. "12,345 elements in 48 categories (active view)".</summary>
        public string Summary
        {
            get
            {
                if (Total == 0) return "No elements found " + ScopeText + ".";
                string elements = Total == 1 ? "1 element" : Total.ToString("N0", CultureInfo.CurrentCulture) + " elements";
                string cats = Categories == 1 ? "1 category" : Categories + " categories";
                return elements + " in " + cats + " " + ScopeText + ".";
            }
        }

        /// <summary>"in the active view" or "in the whole model".</summary>
        public string ScopeText => Scope == CountScope.ActiveView ? "in the active view" : "in the whole model";

        /// <summary>
        /// The report as CSV, so it can be pasted into Excel. The separator is a comma and any value
        /// containing a comma or a quote is quoted, so category names with commas survive.
        /// </summary>
        public string ToCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Category,Elements,Share");
            foreach (CategoryCount r in Rows)
                sb.AppendLine(Csv(r.Category) + "," + r.Count.ToString(CultureInfo.InvariantCulture) + "," +
                              (r.Share * 100.0).ToString("0.0", CultureInfo.InvariantCulture));
            sb.AppendLine(Csv("Total") + "," + Total.ToString(CultureInfo.InvariantCulture) + ",100.0");
            return sb.ToString();
        }

        private static string Csv(string value)
        {
            value = value ?? string.Empty;
            if (value.IndexOf(',') < 0 && value.IndexOf('"') < 0 && value.IndexOf('\n') < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
