// PAC Tools checks - run the logic and the view models without Revit.
using System;
using System.Linq;
using System.Threading.Tasks;
using PacTools.Core;
using PacTools.UI;

namespace PacTools.Checks
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        [STAThread]
        private static int Main()
        {
            Console.WriteLine("PAC Tools - off-screen checks");
            Console.WriteLine(new string('-', 58));

            CountReportChecks();
            ViewModelChecks().GetAwaiter().GetResult();
            ImportChecks.Run();

            Console.WriteLine(new string('-', 58));
            Console.WriteLine($"{_passed} passed, {_failed} failed");
            return _failed == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- Core

        private static void CountReportChecks()
        {
            CountReport r = CountReport.Build(
                new[] { "Walls", "Doors", "Walls", "Walls", "Windows", "Doors" }, CountScope.ActiveView);

            Check("counts are grouped", r.Total == 6 && r.Categories == 3);
            Check("biggest category first", r.Rows[0].Category == "Walls" && r.Rows[0].Count == 3);
            Check("ties sort by name", r.Rows[1].Category == "Doors" && r.Rows[2].Category == "Windows");
            Check("share is worked out", Math.Abs(r.Rows[0].Share - 0.5) < 1e-9);
            Check("shares add up to 1", Math.Abs(r.Rows.Sum(x => x.Share) - 1.0) < 1e-9);
            Check("a big category is counted in full", CountReport.Build(Enumerable.Repeat("Walls", 1500), CountScope.WholeModel)
                                                                    .Rows[0].Count == 1500);

            CountReport blanks = CountReport.Build(new[] { null, "", "   ", "Walls" }, CountScope.ActiveView);
            Check("elements with no category are kept", blanks.Total == 4 && blanks.Categories == 2);
            Check("they are named", blanks.Rows.Any(x => x.Category == CountReport.NoCategory && x.Count == 3));

            Check("names are trimmed together", CountReport.Build(new[] { "Walls", " Walls " }, CountScope.ActiveView)
                                                           .Categories == 1);

            CountReport empty = CountReport.Build(Array.Empty<string>(), CountScope.ActiveView);
            Check("an empty model is not an error", empty.Total == 0 && empty.Rows.Count == 0);
            Check("empty says so", empty.Summary.StartsWith("No elements found", StringComparison.Ordinal));
            Check("a null list is not an error", CountReport.Build(null, CountScope.WholeModel).Total == 0);

            Check("one element reads as singular",
                  CountReport.Build(new[] { "Walls" }, CountScope.ActiveView).Summary.Contains("1 element in 1 category"));
            Check("scope is in the summary", r.Summary.EndsWith("in the active view.", StringComparison.Ordinal));
            Check("whole model says so",
                  CountReport.Build(new[] { "Walls" }, CountScope.WholeModel).Summary.EndsWith("in the whole model.", StringComparison.Ordinal));

            string csv = CountReport.Build(new[] { "Walls, exterior", "Walls, exterior", "Doors" }, CountScope.ActiveView).ToCsv();
            Check("csv has a header row", csv.StartsWith("Category,Elements,Share", StringComparison.Ordinal));
            Check("csv quotes a comma in a name", csv.Contains("\"Walls, exterior\",2,"));
            Check("csv ends with a total", csv.Contains("Total,3,100.0"));
            Check("csv is invariant (a dot, not a comma, for decimals)", !csv.Contains("66,7"));
        }

        // ----------------------------------------------------------- View model

        private static async Task ViewModelChecks()
        {
            var service = new FakeModelService();
            var vm = new ElementCountViewModel(service, new ImmediateRunner());

            Check("starts empty", vm.Rows.Count == 0 && !vm.HasProblem && !vm.IsBusy);
            Check("starts on the active view", vm.IsActiveViewScope && !vm.IsWholeModelScope);

            await vm.RefreshAsync();
            Check("refresh fills the table", vm.Rows.Count == 2 && vm.Rows[0].Category == "Walls");
            Check("refresh asks Revit once", service.Calls.Count == 1);
            Check("refresh sets the header", vm.Header == "Test project  -  Level 1");
            Check("refresh records the time", !string.IsNullOrEmpty(vm.ReadAtText));
            Check("refresh leaves no problem", !vm.HasProblem);
            Check("not busy afterwards", !vm.IsBusy);

            vm.IsWholeModelScope = true;
            await Settle();
            Check("switching scope re-reads", service.Calls.Count == 2 && service.Calls[1] == CountScope.WholeModel);
            Check("the new scope is used", vm.Rows.Sum(x => x.Count) == 5);
            Check("the radio buttons agree", vm.IsWholeModelScope && !vm.IsActiveViewScope);

            vm.IsWholeModelScope = true;
            await Settle();
            Check("re-choosing the same scope does nothing", service.Calls.Count == 2);

            // A problem Revit reported, not a crash.
            service.Throw = new PickProblemException("Switch to a plan view.");
            await vm.RefreshAsync();
            Check("a pick problem is shown, not thrown", vm.HasProblem && vm.ProblemText == "Switch to a plan view.");
            Check("still not busy after a problem", !vm.IsBusy);

            service.Throw = new WrongProjectException("This window belongs to 'Other.rvt'.");
            await vm.RefreshAsync();
            Check("the wrong project is explained", vm.ProblemText.Contains("Other.rvt"));

            service.Throw = new InvalidOperationException("Revit said no.");
            await vm.RefreshAsync();
            Check("an unexpected error is explained, not thrown",
                  vm.ProblemText.StartsWith("The model could not be read:", StringComparison.Ordinal));

            service.Throw = new OperationCanceledException();
            await vm.RefreshAsync();
            Check("a closed window is silent", !vm.HasProblem);

            service.Throw = null;
            await vm.RefreshAsync();
            Check("it recovers after a problem", vm.Rows.Count > 0 && !vm.HasProblem);

            Check("refresh is always offered", vm.RefreshCommand.CanExecute(null));
            Check("copy needs rows", vm.CopyCsvCommand.CanExecute(null));
            Check("report matches the rows", vm.Report.Rows.Count == vm.Rows.Count);
        }

        /// <summary>
        /// Lets a fire-and-forget refresh finish before the next check looks. Choosing a scope starts the
        /// read without awaiting it (that is what the radio buttons do), so the checks have to wait a beat.
        /// </summary>
        private static Task Settle() => Task.Delay(25);

        // --------------------------------------------------------------- Harness

        internal static void Check(string what, bool ok)
        {
            if (ok) { _passed++; Console.WriteLine("  PASS  " + what); }
            else { _failed++; Console.WriteLine("  FAIL  " + what); }
        }
    }
}
