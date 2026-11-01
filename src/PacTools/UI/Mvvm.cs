// PAC Tools - the small MVVM helpers every window uses. No Revit API, no third-party MVVM package.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace PacTools.UI
{
    /// <summary>Minimal <see cref="INotifyPropertyChanged"/> base class for view models.</summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        /// <inheritdoc />
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Sets a backing field and raises PropertyChanged when the value actually changed.</summary>
        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }

        /// <summary>Raises PropertyChanged for one or more property names.</summary>
        protected void Raise(params string[] names)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler == null || names == null) return;
            foreach (string n in names) handler(this, new PropertyChangedEventArgs(n));
        }
    }

    /// <summary>An <see cref="ICommand"/> built from delegates.</summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        /// <summary>Creates the command.</summary>
        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <inheritdoc />
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        /// <inheritdoc />
        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);

        /// <inheritdoc />
        public void Execute(object parameter) => _execute(parameter);
    }

    /// <summary>A value with the label to show for it, for combo boxes.</summary>
    public sealed class Choice<T>
    {
        /// <summary>Creates the choice.</summary>
        public Choice(T value, string label)
        {
            Value = value;
            Label = label;
        }

        /// <summary>The value behind the label.</summary>
        public T Value { get; }

        /// <summary>What the user sees.</summary>
        public string Label { get; }

        /// <inheritdoc />
        public override string ToString() => Label;
    }
}
