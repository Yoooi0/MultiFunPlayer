using System;
using System.Windows.Input;

namespace MultiFunPlayer.Common
{
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;

        public event EventHandler CanExecuteChanged;

        public RelayCommand(Action<T> execute) => _execute = execute;

        public bool CanExecute(object parameter) => true;
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

        public event EventHandler CanExecuteChanged;

        public RelayCommand(Action<T0, T1> execute) => _execute = execute;

        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter)
        {
            if (parameter is not object[] parameters || parameters.Length != 2 || parameters[0] is not T0 || parameters[1] is not T1)
                return;

            _execute((T0)parameters[0], (T1)parameters[1]);
        }
    }
}
