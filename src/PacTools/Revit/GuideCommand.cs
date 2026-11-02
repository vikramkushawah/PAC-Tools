// PAC Tools - the Guide command: opens the user guide PDF that is installed next to the add-in.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PacTools.Core;

namespace PacTools.Revit
{
    /// <summary>
    /// Opens the guide in the PC's PDF reader. Reading the guide needs no project, so this is also its own
    /// availability class: without one, Revit greys the button out until a project is open.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class GuideCommand : IExternalCommand, IExternalCommandAvailability
    {
        /// <inheritdoc />
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;

        /// <inheritdoc />
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            string path = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                Brand.GuideFileName);

            if (!File.Exists(path))
            {
                TaskDialog.Show(Brand.ProductName,
                    "The user guide is missing. Run the PAC Tools installer again.\n\nIt should be here:\n" + path);
                return Result.Cancelled;
            }

            try
            {
                // UseShellExecute hands the file to whichever program Windows opens PDFs with.
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                App.Log("Guide failed to open: " + ex);
                TaskDialog.Show(Brand.ProductName,
                    "Windows could not open the user guide. Check that a PDF reader is installed, " +
                    "or open the file yourself:\n\n" + path);
                return Result.Cancelled;
            }
        }
    }
}
