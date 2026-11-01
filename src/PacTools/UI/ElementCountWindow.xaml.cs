// PAC Tools - the Element Count window. Code-behind holds view concerns only (file dialogs, closing);
// everything else belongs in ElementCountViewModel.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using PacTools.Core;

namespace PacTools.UI
{
    /// <summary>The Element Count window. It stays open while you keep working in Revit.</summary>
    public partial class ElementCountWindow : Window
    {
        private readonly ElementCountViewModel _viewModel;

        /// <summary>Creates the window for a view model.</summary>
        public ElementCountWindow(ElementCountViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeComponent();
            DataContext = _viewModel;
            Loaded += OnLoaded;
        }

        /// <summary>
        /// The first read happens once the window is up, not in the command: the request has to be queued
        /// and Revit only runs the queue when it is idle - which it is not while the command is running.
        /// </summary>
        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            await _viewModel.RefreshAsync();   // RefreshAsync never throws; problems land in ProblemText
        }

        private void SaveCsv_Click(object sender, RoutedEventArgs e)
        {
            CountReport report = _viewModel.Report;
            if (report.Rows.Count == 0)
            {
                MessageBox.Show(this, "There is nothing to save yet. Press Refresh first.",
                                "Element Count", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save the element count",
                Filter = "CSV file (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = ".csv",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = SuggestedFileName()
            };

            if (dialog.ShowDialog(this) != true) return;

            try
            {
                // A BOM makes Excel read the file as UTF-8, so category names keep their accents.
                File.WriteAllText(dialog.FileName, report.ToCsv(), new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The file could not be written:" + Environment.NewLine + Environment.NewLine + ex.Message,
                                "Element Count", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string SuggestedFileName()
        {
            string header = _viewModel.Header ?? string.Empty;
            char[] bad = Path.GetInvalidFileNameChars();
            string clean = new string(header.Select(c => bad.Contains(c) ? '-' : c).ToArray()).Trim();
            if (clean.Length > 60) clean = clean.Substring(0, 60).Trim();
            return string.IsNullOrEmpty(clean) ? "element count.csv" : clean + " - element count.csv";
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

        /// <inheritdoc />
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // If a window ever needs to ask the user something before closing ("save these settings?"),
            // it must not ask while Revit is closing the project. The guard for that is:
            //
            //     if (!ModelessState.IsClosingForProject(this)) { ...ask the user... }
            //
            // This window asks nothing, so there is nothing to guard here.
            base.OnClosing(e);
        }
    }
}
