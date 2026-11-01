// PAC Tools - windows that stay open while you keep working in Revit ("modeless"), and the queue
// that lets them call the Revit API safely.
//
// RULE FOR EVERY PAC TOOLS WINDOW: Revit must never freeze while a tool window is open. That means
//   * the window is shown with ModelessWindows.Show (never Window.ShowDialog), and
//   * every single call into the Revit API from that window goes through the RevitQueue.
// See CONTRIBUTING.md.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using PacTools.Core;

namespace PacTools.Revit
{
    /// <summary>
    /// Sends work from an open tool window to Revit.
    /// <para>
    /// A modeless window may not call the Revit API directly: Revit's API is only valid on its own main
    /// thread, while it is idle. So each request is queued and an <see cref="ExternalEvent"/> is raised;
    /// Revit runs the queue on its main thread as soon as it is idle (after the current command or edit
    /// mode has finished). Every request first checks that the window's project is still the active one.
    /// </para>
    /// <para>
    /// Create it inside an <see cref="IExternalCommand"/> - an ExternalEvent can only be created in the
    /// Revit API context - and dispose it when the window closes (<see cref="ModelessWindows.Show"/>
    /// does that for you).
    /// </para>
    /// </summary>
    public sealed class RevitQueue : IExternalEventHandler, IRevitRunner, IDisposable
    {
        private sealed class Request
        {
            public Action Run;
            public Action<Exception> Fail;
        }

        private readonly ConcurrentQueue<Request> _queue = new ConcurrentQueue<Request>();
        private readonly Document _doc;
        private readonly string _title;
        private readonly string _toolName;
        private readonly Action<string> _log;
        private ExternalEvent _event;

        /// <summary>Creates the queue for one window and the project it was opened on.</summary>
        /// <param name="doc">The project the window works on.</param>
        /// <param name="toolName">The tool's name, used in Revit's event list and in the log.</param>
        /// <param name="log">Where to write unexpected errors; may be null.</param>
        public RevitQueue(Document doc, string toolName, Action<string> log)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _title = doc.Title;
            _toolName = toolName;
            _log = log;
            _event = ExternalEvent.Create(this);
        }

        /// <summary>The project the window works on.</summary>
        public Document Document => _doc;

        /// <inheritdoc />
        public Task<T> RunAsync<T>(Func<T> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));

            // RunContinuationsAsynchronously: the await in the view model resumes on the window's thread
            // AFTER Revit's callback has returned, never inside it.
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_event == null)
            {
                tcs.TrySetException(new OperationCanceledException("The window was closed."));
                return tcs.Task;
            }

            _queue.Enqueue(new Request
            {
                Run = () =>
                {
                    if (tcs.Task.IsCompleted) return;
                    try { tcs.TrySetResult(work()); }
                    catch (Exception ex) { tcs.TrySetException(ex); }
                },
                Fail = ex => tcs.TrySetException(ex)
            });

            ExternalEventRequest state = _event.Raise();
            if (state == ExternalEventRequest.Denied || state == ExternalEventRequest.TimedOut)
                tcs.TrySetException(new InvalidOperationException(
                    "Revit did not accept the request (" + state + "). Finish the current Revit command " +
                    "(or press Esc) and try again."));
            return tcs.Task;
        }

        /// <summary>Called by Revit on its main thread when it is idle. Do not call this yourself.</summary>
        public void Execute(UIApplication app)
        {
            while (_queue.TryDequeue(out Request r))
            {
                try
                {
                    string problem = ProjectProblem(app);
                    if (problem != null) { r.Fail(new WrongProjectException(problem)); continue; }
                    r.Run();
                }
                catch (Exception ex)
                {
                    _log?.Invoke(_toolName + ": unexpected error in a Revit request: " + ex);
                    r.Fail(ex);
                }
            }
        }

        private string ProjectProblem(UIApplication app)
        {
            if (!_doc.IsValidObject)
                return "The project '" + _title + "' was closed. Close this window and start the tool again " +
                       "in the open project.";

            Document active = app.ActiveUIDocument?.Document;
            if (active == null || !active.Equals(_doc))
                return "This window belongs to the project '" + _title + "'. Switch back to that project in Revit " +
                       "(or close this window and start the tool again in the other project). Nothing was changed.";

            return null;
        }

        /// <inheritdoc />
        public string GetName() => Brand.ProductName + " - " + _toolName;

        /// <summary>Cancels requests that have not run yet and releases the ExternalEvent.</summary>
        public void Dispose()
        {
            while (_queue.TryDequeue(out Request r))
                r.Fail(new OperationCanceledException("The window was closed."));
            _event?.Dispose();
            _event = null;
        }
    }

    /// <summary>
    /// Keeps at most one open window per tool. Windows sit on top of Revit (owned by Revit's main window,
    /// so they are not separate taskbar entries), Revit stays usable while they are open, and a window
    /// closes by itself when its project is closed.
    /// </summary>
    public static class ModelessWindows
    {
        private sealed class Entry
        {
            public Window Window;
            public Document Doc;
        }

        private static readonly Dictionary<string, Entry> Open = new Dictionary<string, Entry>();
        private static bool _hooked;

        /// <summary>
        /// Call once from the add-in's <c>OnStartup</c>. Revit's events can only be subscribed to from
        /// OnStartup or from inside a command.
        /// </summary>
        public static void Init(UIControlledApplication application)
        {
            if (_hooked || application == null) return;
            application.ControlledApplication.DocumentClosing += OnDocumentClosing;
            _hooked = true;
        }

        /// <summary>
        /// When this tool's window is already open on this project, brings it to the front and returns true
        /// (so the command can simply return). When it is open on another project it is closed first.
        /// </summary>
        public static bool BringToFront(string key, Document doc)
        {
            if (!Open.TryGetValue(key, out Entry e)) return false;

            if (e.Doc.IsValidObject && e.Doc.Equals(doc)) { Activate(e.Window); return true; }

            e.Window.Close();
            // The window may have refused to close (unsaved work). Then bring it forward instead.
            if (Open.TryGetValue(key, out Entry still) && still.Window == e.Window) { Activate(e.Window); return true; }
            return false;
        }

        /// <summary>Shows the window without blocking Revit. The queue is disposed when the window closes.</summary>
        public static void Show(string key, Window window, UIApplication uiapp, Document doc, RevitQueue queue)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));

            new WindowInteropHelper(window).Owner = uiapp.MainWindowHandle;
            window.ShowInTaskbar = false;
            Open[key] = new Entry { Window = window, Doc = doc };

            window.Closed += (s, e) =>
            {
                if (Open.TryGetValue(key, out Entry x) && x.Window == window) Open.Remove(key);
                ModelessState.SetClosingForProject(window, false);
                queue?.Dispose();
            };

            window.Show();
        }

        /// <summary>Restores and activates a window.</summary>
        public static void Activate(Window window)
        {
            if (window == null) return;
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            if (!window.IsVisible) window.Show();
            window.Activate();
        }

        private static void OnDocumentClosing(object sender, DocumentClosingEventArgs e)
        {
            foreach (Entry x in Open.Values.Where(x => !x.Doc.IsValidObject || x.Doc.Equals(e.Document)).ToList())
            {
                // The window must not ask the user anything while Revit is closing the project.
                ModelessState.SetClosingForProject(x.Window, true);
                x.Window.Close();
            }
        }
    }
}
