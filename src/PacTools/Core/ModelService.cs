// PAC Tools - what a tool needs from the Revit model, expressed without the Revit API.
//
// Every tool should talk to Revit through an interface like this one. The Revit implementation lives
// in Revit/; a fake implementation lives in tests/PacTools.Checks. That is what lets the view models
// be checked off-screen, with no Revit installed.
using System.Collections.Generic;

namespace PacTools.Core
{
    /// <summary>Which elements to count.</summary>
    public enum CountScope
    {
        /// <summary>Only the elements visible in the view that is active in Revit.</summary>
        ActiveView,

        /// <summary>Every model element in the project.</summary>
        WholeModel
    }

    /// <summary>
    /// The model data the Element Count sample tool needs. Implemented by <c>RevitModelService</c> for Revit
    /// and by <c>FakeModelService</c> for the off-screen checks.
    /// </summary>
    public interface IModelService
    {
        /// <summary>The project's title, for the window header.</summary>
        string DocumentTitle { get; }

        /// <summary>The name of the view active in Revit, for the window header.</summary>
        string ActiveViewName { get; }

        /// <summary>
        /// The category name of every element in <paramref name="scope"/>, one entry per element
        /// (so the caller can count them). Elements with no category are reported as null.
        /// </summary>
        IReadOnlyList<string> ReadCategoryNames(CountScope scope);
    }
}
