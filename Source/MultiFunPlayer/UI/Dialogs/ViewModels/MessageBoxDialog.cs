using Stylet;
using System.Windows;

namespace MultiFunPlayer.UI.Dialogs.ViewModels;

internal sealed class MessageBoxDialog(string message, MessageBoxButton buttons) : Screen
{
    public string Message { get; } = message;

    public bool IsOkVisible => buttons is MessageBoxButton.OK or MessageBoxButton.OKCancel;
    public bool IsCancelVisible => buttons is MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel;
    public bool IsYesVisible => buttons is MessageBoxButton.YesNoCancel or MessageBoxButton.YesNo;
    public bool IsNoVisible => buttons is MessageBoxButton.YesNoCancel or MessageBoxButton.YesNo;
}
