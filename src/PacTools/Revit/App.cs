// PAC Tools - the add-in entry point: it builds the ribbon.
//
// TO ADD A TOOL: write its command (see ElementCountCommand.cs), then add one PushButtonData below.
// Nothing else in this file needs to change. See CONTRIBUTING.md, "Adding a new tool".
using System;
using System.IO;
using System.Reflection;
using Autodesk.Revit.UI;
using PacTools.Core;

namespace PacTools.Revit
{
    /// <summary>
    /// Builds the "PAC Tools" ribbon tab when Revit starts. Registered in <c>PacTools.addin</c> as the
    /// add-in's <c>FullClassName</c>.
    /// </summary>
    public sealed class App : IExternalApplication
    {
        /// <summary>The ribbon tab all PAC Tools panels go on.</summary>
        public const string TabName = Brand.TabName;

        /// <inheritdoc />
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                try
                {
                    application.CreateRibbonTab(TabName);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // The tab already exists - another PAC Tools add-in created it. That is fine.
                }

                AddModelPanel(application);
                AddHelpPanel(application);

                // Lets a tool window close itself when its project is closed. Must be called from OnStartup.
                ModelessWindows.Init(application);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Log("Startup failed: " + ex);
                return Result.Failed;
            }
        }

        /// <inheritdoc />
        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

        private static void AddModelPanel(UIControlledApplication application)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "Model");
            string assembly = Assembly.GetExecutingAssembly().Location;

            panel.AddItem(new PushButtonData(
                Brand.ButtonPrefix + "ElementCount",
                "Element\nCount",
                assembly,
                typeof(ElementCountCommand).FullName)
            {
                ToolTip = "Counts the elements in the active view or in the whole model, grouped by category.",
                LongDescription =
                    "Opens a window that stays open while you keep working in Revit. Switch between the active " +
                    "view and the whole model, press Refresh after you change something, and copy the table as " +
                    "CSV for Excel. The model is only read, never changed.",
                LargeImage = Icons.Bars(32),
                Image = Icons.Bars(16)
            });
        }

        private static void AddHelpPanel(UIControlledApplication application)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "Help");
            string assembly = Assembly.GetExecutingAssembly().Location;

            panel.AddItem(new PushButtonData(
                Brand.ButtonPrefix + "About",
                "About",
                assembly,
                typeof(AboutCommand).FullName)
            {
                ToolTip = "Version and contact details for " + Brand.ProductName + ".",
                LargeImage = Icons.Info(32),
                Image = Icons.Info(16)
            });
        }

        /// <summary>The add-in's version, read from the assembly.</summary>
        public static string Version =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        /// <summary>
        /// Appends a line to <c>%LocalAppData%\PACTools\logs\pactools.log</c>. Never throws: a logging
        /// problem must not take Revit down with it.
        /// </summary>
        public static void Log(string message)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Brand.DataFolder, "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "pactools.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
            }
            catch (Exception)
            {
                // Deliberately ignored.
            }
        }
    }
}
