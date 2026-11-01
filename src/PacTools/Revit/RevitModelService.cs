// PAC Tools - the Revit implementation of IModelService for the Element Count sample tool.
//
// This is the only layer that knows about the Revit API. Keep it thin: read what you need, hand plain
// C# values back to Core/ and let Core do the thinking. That is what makes the logic checkable off-screen.
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PacTools.Core;

namespace PacTools.Revit
{
    /// <summary>Reads element categories out of the open Revit project.</summary>
    public sealed class RevitModelService : IModelService
    {
        private readonly UIDocument _uidoc;

        /// <summary>Creates the service for the open project.</summary>
        public RevitModelService(UIDocument uidoc)
        {
            _uidoc = uidoc;
        }

        private Document Doc => _uidoc.Document;

        /// <inheritdoc />
        public string DocumentTitle => Doc?.Title ?? string.Empty;

        /// <inheritdoc />
        public string ActiveViewName => _uidoc?.ActiveView?.Name ?? "(no view)";

        /// <inheritdoc />
        /// <remarks>Must be called inside the Revit API context - i.e. through the <see cref="RevitQueue"/>.</remarks>
        public IReadOnlyList<string> ReadCategoryNames(CountScope scope)
        {
            var names = new List<string>();

            FilteredElementCollector collector;
            if (scope == CountScope.ActiveView)
            {
                View view = _uidoc.ActiveView;
                if (view == null)
                    throw new PickProblemException("There is no active view. Open a view in Revit and try again.");

                try
                {
                    collector = new FilteredElementCollector(Doc, view.Id);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // Some view types (schedules, for instance) cannot be collected from.
                    throw new PickProblemException(
                        "The elements of the view '" + view.Name + "' cannot be counted. " +
                        "Switch to a plan, section, elevation or 3D view, or choose \"Whole model\".");
                }
            }
            else
            {
                collector = new FilteredElementCollector(Doc);
            }

            foreach (Element e in collector.WhereElementIsNotElementType())
            {
                Category category = e.Category;
                names.Add(category?.Name);
            }

            return names;
        }
    }
}
