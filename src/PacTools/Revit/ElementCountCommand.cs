// PAC Tools - the Element Count command. This is the worked example: copy this file to make a new tool.
//
// The shape of every PAC Tools command with a window is the same:
//   1. check there is an open project;
//   2. if the window is already open, bring it to the front and return;
//   3. build the Revit service + a RevitQueue + the view model;
//   4. hand the window to ModelessWindows.Show.
using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PacTools.Core;
using PacTools.UI;

namespace PacTools.Revit
{
    /// <summary>
    /// Opens the Element Count window. The window stays open while you keep working in Revit; every call
    /// into Revit goes through a <see cref="RevitQueue"/>. Pressing the ribbon button again brings the open
    /// window to the front instead of opening a second one.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ElementCountCommand : IExternalCommand
    {
        /// <summary>Identifies this tool's single window. One short key per tool; they must be unique.</summary>
        private const string WindowKey = "ElementCount";

        /// <summary>The tool's name, used in the window title, the Revit event list and the log.</summary>
        public const string ToolName = "Element Count";

        /// <inheritdoc />
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                UIDocument uidoc = uiapp.ActiveUIDocument;

                if (uidoc?.Document == null)
                {
                    message = "Open a Revit project first.";
                    return Result.Failed;
                }

                if (uidoc.Document.IsFamilyDocument)
                {
                    message = ToolName + " works in project documents, not in the family editor.";
                    return Result.Failed;
                }

                if (ModelessWindows.BringToFront(WindowKey, uidoc.Document))
                    return Result.Succeeded;

                var service = new RevitModelService(uidoc);
                var queue = new RevitQueue(uidoc.Document, ToolName, App.Log);
                var viewModel = new ElementCountViewModel(service, queue);

                ModelessWindows.Show(WindowKey, new ElementCountWindow(viewModel), uiapp, uidoc.Document, queue);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                App.Log(ToolName + " failed to start: " + ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
