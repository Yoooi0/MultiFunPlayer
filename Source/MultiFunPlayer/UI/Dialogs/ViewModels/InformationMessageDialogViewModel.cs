using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using Stylet;
using System.Diagnostics;

namespace MultiFunPlayer.UI.Dialogs.ViewModels;

internal class InformationMessageDialogViewModel(bool showCheckbox) : Screen
{
    public string VersionText => $"v{ReflectionUtils.AssemblyInformationalVersion}";
    public bool ShowCheckbox { get; } = showCheckbox;
    public bool DontShowAgain { get; set; }

    public void OnDismiss()
    {
        DialogHost.CloseDialogCommand.Execute(ShowCheckbox ? DontShowAgain : null, null);
    }

    public void OnNavigate(string target)
    {
        if (!Uri.IsWellFormedUriString(target, UriKind.Absolute))
            return;

        Process.Start(new ProcessStartInfo()
        {
            FileName = target,
            UseShellExecute = true
        });
    }

    public override bool Equals(object obj) => obj != null && GetType() == obj.GetType();
    public override int GetHashCode() => HashCode.Combine(GetType());
}
