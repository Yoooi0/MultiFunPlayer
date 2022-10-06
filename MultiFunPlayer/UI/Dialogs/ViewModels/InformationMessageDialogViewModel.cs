using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using Stylet;
using System.Diagnostics;
using System.Windows.Navigation;

namespace MultiFunPlayer.UI.Dialogs.ViewModels;

public class InformationMessageDialogViewModel : Screen
{
    public string VersionText => $"v{ReflectionUtils.AssemblyVersion}";
    public bool ShowCheckbox { get; }
    public bool DontShowAgain { get; set; }

    public InformationMessageDialogViewModel(bool showCheckbox)
    {
        ShowCheckbox = showCheckbox;
    }

    public void OnDismiss()
    {
        DialogHost.CloseDialogCommand.Execute(ShowCheckbox ? DontShowAgain : null, null);
    }

    public void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo()
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }

    public override bool Equals(object obj)
        => obj != null && GetType() == obj.GetType();

    public override int GetHashCode() => VersionText.GetHashCode();
}
