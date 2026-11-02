// PAC Tools - what the Revit layer must provide, expressed without the Revit API.
//
// These four interfaces are the whole boundary between Core/ and Revit/. Everything above them - mapping,
// validation, diffing, the wizard - is driven in the off-screen checks by fakes that implement them. The
// carrier is behind ICarrierStore on purpose: if the M0 spike shows key-schedule rows can be created
// through the API, a second implementation is all that changes.
using System;
using System.Collections.Generic;
using System.Linq;

namespace PacTools.Core
{
    /// <summary>One carrier element in the model, as the diff needs to see it.</summary>
    public sealed class CarrierRow
    {
        /// <summary>Creates the row.</summary>
        public CarrierRow(string elementRef, string key, int order, string hash,
                          IReadOnlyDictionary<string, string> values)
        {
            ElementRef = elementRef;
            Key = key;
            Order = order;
            Hash = hash;
            Values = values ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Identifies the element to the Revit layer, which is the only thing that interprets it (it holds
        /// an ElementId). Core never looks inside it.
        /// </summary>
        public string ElementRef { get; }

        /// <summary>The row's key, from <c>PAC_RowKey</c>.</summary>
        public string Key { get; }

        /// <summary>The row's position, from <c>PAC_RowOrder</c>.</summary>
        public int Order { get; }

        /// <summary>The hash of the cells this row was last imported from, from <c>PAC_RowHash</c>.</summary>
        public string Hash { get; }

        /// <summary>The stored values, by pool parameter name.</summary>
        public IReadOnlyDictionary<string, string> Values { get; }

        /// <summary>True when the element is borrowed by someone else and cannot be changed right now.</summary>
        public string BorrowedBy { get; set; }

        /// <summary>True when this row can be written to.</summary>
        public bool CanWrite => string.IsNullOrEmpty(BorrowedBy);
    }

    /// <summary>What actually happened when a diff was applied.</summary>
    public sealed class ApplyResult
    {
        /// <summary>How many carriers were created.</summary>
        public int Added { get; set; }

        /// <summary>How many had values changed.</summary>
        public int Updated { get; set; }

        /// <summary>How many were deleted.</summary>
        public int Removed { get; set; }

        /// <summary>Rows that could not be touched, with the reason - a borrowed element, for instance.</summary>
        public List<string> Skipped { get; } = new List<string>();

        /// <summary>True when the model was changed at all. When false the transaction should be rolled back.</summary>
        public bool AnyChange => Added > 0 || Updated > 0 || Removed > 0;

        /// <summary>The line shown after a refresh, including what was skipped and why.</summary>
        public string Summary
        {
            get
            {
                if (!AnyChange && Skipped.Count == 0) return "Nothing changed.";

                var parts = new List<string>();
                if (Updated > 0) parts.Add(Updated + " updated");
                if (Added > 0) parts.Add(Added + " added");
                if (Removed > 0) parts.Add(Removed + " removed");

                string done = parts.Count > 0 ? string.Join(", ", parts) : "Nothing changed";
                string skipped = Skipped.Count > 0
                    ? "  " + Skipped.Count + " row(s) were skipped: " + string.Join("; ", Skipped.Take(5)) +
                      (Skipped.Count > 5 ? " ..." : string.Empty)
                    : string.Empty;

                return done + "." + skipped + (AnyChange ? "  Undo: Ctrl+Z." : string.Empty);
            }
        }
    }

    /// <summary>Creates, reads and updates the carrier elements that hold the spreadsheet's rows.</summary>
    public interface ICarrierStore
    {
        /// <summary>
        /// Makes sure the pool's shared parameters exist and are bound to the carrier category. Called once,
        /// inside the import transaction, before any carrier is written.
        /// </summary>
        void EnsureParameters(ImportLimits limits);

        /// <summary>Every carrier belonging to one import, which is the previous state a refresh diffs against.</summary>
        IReadOnlyList<CarrierRow> Read(Guid dataSetId);

        /// <summary>
        /// Applies a diff. Must be called inside one transaction so the whole refresh is one undo step.
        /// <paramref name="removeMissing"/> is false unless the user confirmed the removals.
        /// </summary>
        ApplyResult Apply(Guid dataSetId, DiffResult diff, ColumnMap map, ImportLimits limits, bool removeMissing);

        /// <summary>Deletes every carrier of one import, for Unlink and Delete. Returns how many went.</summary>
        int DeleteAll(Guid dataSetId);
    }

    /// <summary>Creates and maintains the Revit schedule that presents one import.</summary>
    public interface IScheduleBuilder
    {
        /// <summary>
        /// Creates the schedule: a schedule of the carrier category, filtered to this import's
        /// <c>PAC_DataSetId</c>, with one field per included column. Returns a reference the link record stores.
        /// </summary>
        string Create(Guid dataSetId, string scheduleName, ColumnMap map, ImportLimits limits);

        /// <summary>
        /// Writes each column's display name onto its schedule field. This is what lets the user see
        /// "Door Reference" where the parameter is PAC_T03, and what makes a renamed heading a cheap change.
        /// </summary>
        void ApplyHeadings(string scheduleRef, ColumnMap map);

        /// <summary>True when the schedule is still in the project - the user may have deleted it.</summary>
        bool Exists(string scheduleRef);
    }

    /// <summary>Stores the link records in the project (Extensible Storage on a DataStorage element).</summary>
    public interface ILinkStore
    {
        /// <summary>Every link in the project, for the Manage links window.</summary>
        IReadOnlyList<LinkRecord> ReadAll();

        /// <summary>One link, or null.</summary>
        LinkRecord Read(Guid dataSetId);

        /// <summary>Writes a link, replacing any record with the same id. Inside a transaction.</summary>
        void Save(LinkRecord record);

        /// <summary>Forgets a link. The carriers and the schedule are dealt with separately.</summary>
        void Delete(Guid dataSetId);
    }
}
