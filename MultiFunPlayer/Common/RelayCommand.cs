using System;
using System.Windows.Input;

namespace MultiFunPlayer.Common
{
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public RelayCommand(Action<T> execute) : this(execute, null) { }
        public RelayCommand(Action<T> execute, Func<T, bool> canExecute)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            if (_canExecute == null)
                return true;

            if (parameter is not T)
                return false;

            return _canExecute((T)parameter);
        }

        public void Execute(object parameter)
        {
            if (parameter is not T)
                return;

            _execute((T)parameter);
        }
    }

    public class RelayCommand<T0, T1> : ICommand
    {
        private readonly Action<T0, T1> _execute;
        private readonly Func<T0, T1, bool> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public RelayCommand(Action<T0, T1> execute) : this(execute, null) { }
        public RelayCommand(Action<T0, T1> execute, Func<T0, T1, bool> canExecute)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            if (_canExecute == null)
                return true;

            if (parameter is not object[] parameters || parameters.Length != 2 || parameters[0] is not T0 || parameters[1] is not T1)
                return false;

            return _canExecute((T0)parameters[0], (T1)parameters[1]);
        }

        public void Execute(object parameter)
        {
            if (parameter is not object[] parameters || parameters.Length != 2 || parameters[0] is not T0 || parameters[1] is not T1)
                return;

            _execute((T0)parameters[0], (T1)parameters[1]);
        }
    }
}
