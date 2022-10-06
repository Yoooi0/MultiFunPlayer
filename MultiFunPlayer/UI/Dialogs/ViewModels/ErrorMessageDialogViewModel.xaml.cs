using Stylet;

namespace MultiFunPlayer.UI.Dialogs.ViewModels;

public class ErrorMessageDialogViewModel : Screen
{
    public string Message { get; }

    public ErrorMessageDialogViewModel(string message) => Message = message;
    public ErrorMessageDialogViewModel(Exception exception, string message) => Message = $"{message}:\n\n{exception}";

    public override bool Equals(object obj)
        => obj != null && GetType() == obj.GetType() && GetHashCode() == obj.GetHashCode();

    public override int GetHashCode() => Message.GetHashCode();
}
