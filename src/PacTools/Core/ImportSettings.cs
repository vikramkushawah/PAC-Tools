// PAC Tools - the project-wide choices, in one place.
//
// These are the five decisions listed in docs/ARCHITECTURE.md section 11. They are written here as
// defaults so work can continue before they are confirmed: changing one is a one-line edit, not a redesign.
// Every default below is marked with the decision it settles and why that value was chosen.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PacTools.Core
{
    /// <summary>The limits and defaults an import works to.</summary>
    public sealed class ImportLimits
    {
        /// <summary>The values PAC Tools ships with. Copy and change it rather than editing in place.</summary>
        public static ImportLimits Default => new ImportLimits();

        /// <summary>
        /// DECISION 1 - the Revit category the carrier elements belong to. Generic Models is chosen because
        /// every project has it, it schedules cleanly and it accepts shared parameters. The cost is that the
        /// carriers show up in Generic Models counts elsewhere, which one schedule filter removes.
        /// </summary>
        public string CarrierCategoryName { get; set; } = "Generic Models";

        /// <summary>DECISION 3 - above this many rows the user is warned before the model is touched.</summary>
        public int RowWarningThreshold { get; set; } = 5000;

        /// <summary>A refusal, not a warning: beyond this an import would make the model unusable.</summary>
        public int RowHardLimit { get; set; } = 50000;

        /// <summary>
        /// DECISION 4 - false means a row key is optional and row order is used when none is chosen.
        /// Row order is convenient but fragile: inserting a row in Excel re-labels every row below it on the
        /// next refresh. The wizard therefore recommends a key column and says why.
        /// </summary>
        public bool RequireKeyColumn { get; set; } = false;

        /// <summary>
        /// DECISION 5 - PAC Tools owns its own shared parameter file rather than appending to TCPL's.
        /// Keeping it separate means an outsourced build can never corrupt the office file; the cost is a
        /// second file to keep with the project.
        /// </summary>
        public string SharedParameterFileName { get; set; } = "PAC_Tools_Shared_Parameters.txt";

        /// <summary>The workset carriers are put on when the model is workshared.</summary>
        public string CarrierWorksetName { get; set; } = "PAC Data";

        /// <summary>How many bad cells are listed individually before the rest are summarised.</summary>
        public int MaxReportedCellProblems { get; set; } = 50;

        /// <summary>How many rows the wizard's preview grid shows.</summary>
        public int PreviewRows { get; set; } = 20;

        /// <summary>DECISION 2 - how many columns of each type the parameter pool provides.</summary>
        public IReadOnlyDictionary<ColumnDataType, int> Capacity { get; set; } = ParameterPool.DefaultCapacity;

        /// <summary>How many columns of <paramref name="type"/> an import may use.</summary>
        public int CapacityOf(ColumnDataType type) => Capacity.TryGetValue(type, out int n) ? n : 0;
    }

    /// <summary>
    /// The fixed pool of shared parameters the carriers store their values in.
    /// <para>
    /// This is the heart of the design. Revit's parameter bindings are per category and project-wide, so a
    /// shared parameter per spreadsheet column would add a parameter to every Generic Model in the project,
    /// without limit, every time anyone imported a sheet. Instead there is a fixed pool - 42 parameters,
    /// however many spreadsheets are imported - and each schedule renames its own columns through
    /// <c>ScheduleField.ColumnHeading</c>. The user sees "Door Reference"; the parameter is PAC_T03.
    /// </para>
    /// <para>
    /// The cost is a hard cap on columns per type. That is a deliberate trade: a sheet wider than the pool
    /// is refused with a clear message, which is better than an unbounded parameter list in every project.
    /// </para>
    /// </summary>
    public static class ParameterPool
    {
        /// <summary>DECISION 2 - the shipped pool sizes.</summary>
        public static readonly IReadOnlyDictionary<ColumnDataType, int> DefaultCapacity =
            new Dictionary<ColumnDataType, int>
            {
                [ColumnDataType.Text] = 24,
                [ColumnDataType.Number] = 8,
                [ColumnDataType.Integer] = 4,
                [ColumnDataType.Length] = 4,
                [ColumnDataType.Area] = 2,
                [ColumnDataType.YesNo] = 2
            };

        /// <summary>Every parameter name begins with this, so the pool is obvious in Revit's parameter list.</summary>
        public const string Prefix = "PAC_";

        /// <summary>The parameter that identifies which import a carrier belongs to. Never part of the pool.</summary>
        public const string DataSetIdParameter = "PAC_DataSetId";

        /// <summary>The parameter holding a carrier's row key, so a refresh can match rows without reading every column.</summary>
        public const string RowKeyParameter = "PAC_RowKey";

        /// <summary>The parameter holding a carrier's row order, used when no key column was chosen.</summary>
        public const string RowOrderParameter = "PAC_RowOrder";

        /// <summary>
        /// A hash of the row's source cells, written with the row. A refresh compares hashes rather than
        /// values, which sidesteps the one real trap in diffing: a number written back by Revit does not
        /// read back as the text that was imported ("0.10" becomes "0.1"), so comparing displayed values
        /// reports changes that did not happen. The hash is computed from the spreadsheet text, both times.
        /// </summary>
        public const string RowHashParameter = "PAC_RowHash";

        /// <summary>The one-letter code in a slot name for each type.</summary>
        public static string Code(ColumnDataType type)
        {
            switch (type)
            {
                case ColumnDataType.Text: return "T";
                case ColumnDataType.Number: return "N";
                case ColumnDataType.Integer: return "I";
                case ColumnDataType.Length: return "L";
                case ColumnDataType.Area: return "A";
                case ColumnDataType.YesNo: return "Y";
                default: throw new ArgumentOutOfRangeException(nameof(type), type, "No pool code for this type.");
            }
        }

        /// <summary>
        /// The parameter name for one slot, e.g. <c>PAC_T03</c>. <paramref name="oneBasedIndex"/> starts at 1.
        /// </summary>
        public static string SlotName(ColumnDataType type, int oneBasedIndex)
        {
            if (oneBasedIndex < 1) throw new ArgumentOutOfRangeException(nameof(oneBasedIndex));
            return Prefix + Code(type) + oneBasedIndex.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>Every slot name for a type, in order.</summary>
        public static IEnumerable<string> Slots(ColumnDataType type, int capacity) =>
            Enumerable.Range(1, Math.Max(0, capacity)).Select(i => SlotName(type, i));

        /// <summary>
        /// Every parameter the project needs for PAC Tools to work: the three bookkeeping parameters and
        /// the whole pool. These are created once, when the first import runs.
        /// </summary>
        public static IEnumerable<string> AllParameterNames(ImportLimits limits)
        {
            limits = limits ?? ImportLimits.Default;
            yield return DataSetIdParameter;
            yield return RowKeyParameter;
            yield return RowOrderParameter;
            yield return RowHashParameter;

            foreach (ColumnDataType type in DefaultCapacity.Keys)
                foreach (string slot in Slots(type, limits.CapacityOf(type)))
                    yield return slot;
        }

        /// <summary>True when <paramref name="name"/> is one of PAC Tools' own parameters.</summary>
        public static bool IsPacParameter(string name) =>
            !string.IsNullOrEmpty(name) && name.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>
        /// Hands out slots for a set of columns, in the order they are asked for. Returns false and names
        /// the type that ran out, so the wizard can say "this sheet has 31 text columns; the limit is 24".
        /// </summary>
        public static bool TryAllocate(IEnumerable<ColumnDataType> types, ImportLimits limits,
                                       out IReadOnlyList<string> slots, out string problem)
        {
            limits = limits ?? ImportLimits.Default;
            var used = new Dictionary<ColumnDataType, int>();
            var result = new List<string>();

            foreach (ColumnDataType type in types ?? Enumerable.Empty<ColumnDataType>())
            {
                used.TryGetValue(type, out int already);
                int next = already + 1;
                int capacity = limits.CapacityOf(type);

                if (next > capacity)
                {
                    slots = null;
                    problem = "This selection needs " + next + " " + ValueParser.Label(type).ToLowerInvariant() +
                              " column(s); PAC Tools provides " + capacity + ". " +
                              "Leave some out, or change the type of a column.";
                    return false;
                }

                used[type] = next;
                result.Add(SlotName(type, next));
            }

            slots = result;
            problem = null;
            return true;
        }
    }
}
