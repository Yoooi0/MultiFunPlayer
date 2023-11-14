using System.Windows.Input;

namespace MultiFunPlayer.UI;

public class RelayCommand<T>(Action<T> execute, Func<T, bool> canExecute) : ICommand
{
    public event EventHandler CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public RelayCommand(Action<T> execute) : this(execute, null) { }

    public bool CanExecute(object parameter)
    {
        if (canExecute == null)
            return true;

        if (parameter is not T)
            return false;

        return canExecute((T)parameter);
    }

    public void Execute(object parameter)
    {
        if (parameter is not T)
            return;

        execute((T)parameter);
    }
}

public class RelayCommand<T0, T1>(Action<T0, T1> execute, Func<T0, T1, bool> canExecute) : ICommand
{
    public event EventHandler CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public RelayCommand(Action<T0, T1> execute) : this(execute, null) { }

    public bool CanExecute(object parameter)
    {
        if (canExecute == null)
            return true;

        if (parameter is not object[] parameters || parameters.Length != 2 || parameters[0] is not T0 || parameters[1] is not T1)
            return false;

        return canExecute((T0)parameters[0], (T1)parameters[1]);
    }

    public void Execute(object parameter)
    {
        if (parameter is not object[] parameters || parameters.Length != 2 || parameters[0] is not T0 || parameters[1] is not T1)
            return;

        execute((T0)parameters[0], (T1)parameters[1]);
    }
}
