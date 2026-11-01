// PAC Tools - running work where the Revit API may be used.
//
// This file contains NO Revit API types on purpose, so the view models and the off-screen
// checks in tests/PacTools.Checks can use it without Revit being installed.
// The Revit implementation of IRevitRunner is RevitQueue, in Revit/Modeless.cs.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PacTools.Core
{
    /// <summary>
    /// Runs work in a place where the Revit API may legally be called.
    /// <para>
    /// A modeless window runs on its own WPF thread and may <b>not</b> touch the Revit API directly.
    /// In Revit this interface is implemented by <c>RevitQueue</c>, which parks the work until Revit is
    /// idle and then runs it on Revit's main thread - so the window never freezes Revit. In the
    /// off-screen checks it is implemented by <see cref="ImmediateRunner"/>, which just runs the work.
    /// </para>
    /// </summary>
    public interface IRevitRunner
    {
        /// <summary>Runs <paramref name="work"/> in the Revit API context; the task completes on the caller's thread.</summary>
        Task<T> RunAsync<T>(Func<T> work);
    }

    /// <summary>Runs the work straight away. For the off-screen checks, and for code already in the Revit API context.</summary>
    public sealed class ImmediateRunner : IRevitRunner
    {
        /// <inheritdoc />
        public Task<T> RunAsync<T>(Func<T> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            try { return Task.FromResult(work()); }
            catch (Exception ex) { return Task.FromException<T>(ex); }
        }
    }

    /// <summary>Convenience helpers for <see cref="IRevitRunner"/>.</summary>
    public static class RevitRunnerExtensions
    {
        /// <summary>Runs work that returns nothing.</summary>
        public static Task RunAsync(this IRevitRunner runner, Action work)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            if (work == null) throw new ArgumentNullException(nameof(work));
            return runner.RunAsync(() => { work(); return true; });
        }
    }

    /// <summary>
    /// The window belongs to a different project than the one now active in Revit (or its project was closed).
    /// The message tells the user what to do; nothing in the model was changed.
    /// </summary>
    public sealed class WrongProjectException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public WrongProjectException(string message) : base(message) { }
    }

    /// <summary>
    /// The user pressed Esc, or Revit refused a pick. Shown to the user as a plain message,
    /// never as an error - picking being cancelled is normal.
    /// </summary>
    public sealed class PickProblemException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public PickProblemException(string message) : base(message) { }
    }

    /// <summary>
    /// Marks the tool windows that are closing because their Revit project is closing.
    /// A window must not ask the user anything then ("save first?"), because Revit is mid-close.
    /// </summary>
    public static class ModelessState
    {
        private static readonly HashSet<object> ClosingForProject = new HashSet<object>();

        /// <summary>True while <paramref name="window"/> is being closed because its project is closing.</summary>
        public static bool IsClosingForProject(object window) => window != null && ClosingForProject.Contains(window);

        /// <summary>Marks / unmarks a window. Called by the Revit side only.</summary>
        public static void SetClosingForProject(object window, bool closing)
        {
            if (window == null) return;
            if (closing) ClosingForProject.Add(window); else ClosingForProject.Remove(window);
        }
    }
}
