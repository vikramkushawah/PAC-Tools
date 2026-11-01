// PAC Tools - the Element Count window's view model.
//
// It knows nothing about the Revit API. It talks to IModelService (what to read) through IRevitRunner
// (when it is legal to read it). That is what lets tests/PacTools.Checks drive this whole class with a
// fake service and no Revit installed - and it is why the window never freezes Revit.
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Input;
using PacTools.Core;

namespace PacTools.UI
{
    /// <summary>Drives the Element Count window.</summary>
    public sealed class ElementCountViewModel : ObservableObject
    {
        /// <summary>Everything one refresh reads from Revit, so a refresh is a single queued request.</summary>
        private sealed class Snapshot
        {
            public string Title;
            public string ViewName;
            public IReadOnlyList<string> Names;
        }

        private readonly IModelService _service;
        private readonly IRevitRunner _runner;
        private readonly Action<string> _log;

        private CountScope _scope = CountScope.ActiveView;
        private CountReport _report = CountReport.Empty;
        private string _header;
        private string _problemText;
        private string _readAtText;
        private bool _isBusy;

        /// <summary>Creates the view model.</summary>
        /// <param name="service">Reads the model. The Revit one, or a fake in the off-screen checks.</param>
        /// <param name="runner">Where Revit calls are allowed to happen. <c>RevitQueue</c> in Revit.</param>
        /// <param name="log">Optional logger for unexpected errors.</param>
        public ElementCountViewModel(IModelService service, IRevitRunner runner, Action<string> log = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _log = log;

            Rows = new ObservableCollection<CategoryCount>();
            RefreshCommand = new RelayCommand(_ => Fire(), _ => !IsBusy);
            CopyCsvCommand = new RelayCommand(_ => CopyCsv(), _ => Rows.Count > 0);
        }

        /// <summary>The rows shown in the table, biggest category first.</summary>
        public ObservableCollection<CategoryCount> Rows { get; }

        /// <summary>Reads the model again. Disabled while a read is in flight.</summary>
        public ICommand RefreshCommand { get; }

        /// <summary>Puts the table on the clipboard as CSV. Disabled while the table is empty.</summary>
        public ICommand CopyCsvCommand { get; }

        /// <summary>The report behind <see cref="Rows"/>. Used by the window to save a CSV file.</summary>
        public CountReport Report => _report;

        /// <summary>"Project title - view name", shown under the window's heading.</summary>
        public string Header
        {
            get => _header;
            private set => Set(ref _header, value);
        }

        /// <summary>A one-line summary of the last read, e.g. "4,201 elements in 36 categories in the active view."</summary>
        public string SummaryText => _report.Summary;

        /// <summary>When the model was last read, e.g. "Read at 15:04:22". Empty before the first read.</summary>
        public string ReadAtText
        {
            get => _readAtText;
            private set => Set(ref _readAtText, value);
        }

        /// <summary>Why the last read did not work, or null. Shown in the red box.</summary>
        public string ProblemText
        {
            get => _problemText;
            private set
            {
                if (Set(ref _problemText, value)) Raise(nameof(HasProblem));
            }
        }

        /// <summary>True while there is something to show in the red box.</summary>
        public bool HasProblem => !string.IsNullOrEmpty(_problemText);

        /// <summary>True while a read is waiting for Revit. The buttons are disabled meanwhile.</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (!Set(ref _isBusy, value)) return;
                Raise(nameof(IsNotBusy));
                // RelayCommand only re-asks CanExecute when the CommandManager says so. IsBusy changes
                // while nothing is being clicked, so the buttons have to be told to look again.
                CommandManager.InvalidateRequerySuggested();
            }
        }

        /// <summary>The opposite of <see cref="IsBusy"/>, for enabling controls in XAML.</summary>
        public bool IsNotBusy => !_isBusy;

        /// <summary>True when the "Active view" radio button is chosen.</summary>
        public bool IsActiveViewScope
        {
            get => _scope == CountScope.ActiveView;
            set
            {
                if (!value || _scope == CountScope.ActiveView) return;
                _scope = CountScope.ActiveView;
                Raise(nameof(IsActiveViewScope), nameof(IsWholeModelScope));
                Fire();
            }
        }

        /// <summary>True when the "Whole model" radio button is chosen.</summary>
        public bool IsWholeModelScope
        {
            get => _scope == CountScope.WholeModel;
            set
            {
                if (!value || _scope == CountScope.WholeModel) return;
                _scope = CountScope.WholeModel;
                Raise(nameof(IsActiveViewScope), nameof(IsWholeModelScope));
                Fire();
            }
        }

        /// <summary>
        /// Reads the model and fills the table. It never throws: a problem ends up in
        /// <see cref="ProblemText"/> instead, so the window can await it safely.
        /// </summary>
        public async Task RefreshAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            ProblemText = null;
            CountScope scope = _scope;

            try
            {
                // One queued request per refresh: everything Revit has to answer is read together.
                Snapshot snap = await _runner.RunAsync(() => new Snapshot
                {
                    Title = _service.DocumentTitle,
                    ViewName = _service.ActiveViewName,
                    Names = _service.ReadCategoryNames(scope)
                });

                _report = CountReport.Build(snap.Names, scope);

                Rows.Clear();
                foreach (CategoryCount row in _report.Rows) Rows.Add(row);

                Header = string.IsNullOrEmpty(snap.Title) ? snap.ViewName : snap.Title + "  -  " + snap.ViewName;
                ReadAtText = "Read at " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
                Raise(nameof(SummaryText), nameof(Report));
            }
            catch (OperationCanceledException)
            {
                // The window was closed while the request was queued. Nothing to report.
            }
            catch (WrongProjectException ex)
            {
                ProblemText = ex.Message;
            }
            catch (PickProblemException ex)
            {
                ProblemText = ex.Message;
            }
            catch (Exception ex)
            {
                _log?.Invoke("Element Count refresh failed: " + ex);
                ProblemText = "The model could not be read: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Starts a refresh without waiting for it. Safe because <see cref="RefreshAsync"/> never throws.</summary>
        private void Fire()
        {
            Task ignored = RefreshAsync();
            GC.KeepAlive(ignored);
        }

        private void CopyCsv()
        {
            try
            {
                System.Windows.Clipboard.SetText(_report.ToCsv());
                ProblemText = null;
                ReadAtText = "Copied as CSV - paste it into Excel.";
            }
            catch (Exception ex)
            {
                // The clipboard belongs to the whole of Windows; another program can be holding it.
                _log?.Invoke("Clipboard copy failed: " + ex);
                ProblemText = "Windows would not give up the clipboard. Try again in a moment.";
            }
        }
    }
}
