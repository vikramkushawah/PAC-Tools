// PAC Tools checks - a stand-in for Revit.
using System;
using System.Collections.Generic;
using PacTools.Core;

namespace PacTools.Checks
{
    /// <summary>Answers like Revit would, but from a list set up by the test.</summary>
    internal sealed class FakeModelService : IModelService
    {
        public string DocumentTitle { get; set; } = "Test project";
        public string ActiveViewName { get; set; } = "Level 1";

        /// <summary>What ReadCategoryNames returns for each scope.</summary>
        public Dictionary<CountScope, List<string>> ByScope { get; } = new Dictionary<CountScope, List<string>>
        {
            [CountScope.ActiveView] = new List<string> { "Walls", "Walls", "Doors" },
            [CountScope.WholeModel] = new List<string> { "Walls", "Walls", "Walls", "Doors", "Windows" }
        };

        /// <summary>When set, ReadCategoryNames throws this instead of answering.</summary>
        public Exception Throw { get; set; }

        /// <summary>How many times Revit was asked, and what it was asked for.</summary>
        public List<CountScope> Calls { get; } = new List<CountScope>();

        public IReadOnlyList<string> ReadCategoryNames(CountScope scope)
        {
            Calls.Add(scope);
            if (Throw != null) throw Throw;
            return ByScope[scope];
        }
    }
}
