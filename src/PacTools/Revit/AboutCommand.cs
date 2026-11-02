// PAC Tools - the About command. The simplest kind of tool: no window, no view model, just a dialog.
using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PacTools.Core;

namespace PacTools.Revit
{
    /// <summary>Shows the add-in's version and where to find the log.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class AboutCommand : IExternalCommand
    {
        /// <inheritdoc />
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var dialog = new TaskDialog(Brand.ProductName)
                {
                    MainInstruction = Brand.ProductName + "  " + App.Version,
                    MainContent =
                        "Revit add-in for Tandon Consultants Pvt. Ltd. (TCPL).\n\n" +
                        "Tools on this tab:\n" +
                        "    •  Element Count – elements per category, for the active view or the whole model.\n" +
                        "    •  Guide – the user guide, as a PDF.\n\n" +
                        "Log file:\n" +
                        "    %LocalAppData%\\" + Brand.DataFolder + "\\logs\\pactools.log",
                    CommonButtons = TaskDialogCommonButtons.Close,
                    DefaultButton = TaskDialogResult.Close
                };
                dialog.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                App.Log("About failed: " + ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
