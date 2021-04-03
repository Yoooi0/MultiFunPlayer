using MaterialDesignThemes.Wpf;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace MultiFunPlayer.Common.Controls
{
    /// <summary>
    /// Interaction logic for InformationMessageDialog.xaml
    /// </summary>
    public partial class InformationMessageDialog : UserControl
    {
        public string VersionText => $"v{Assembly.GetEntryAssembly().GetName().Version}";
        public bool ShowCheckbox { get; }
        public bool DontShowAgain { get; set; }

        public InformationMessageDialog(bool showCheckbox)
        {
            ShowCheckbox = showCheckbox;

            InitializeComponent();
        }

        public void OnDismiss()
        {
            DialogHost.CloseDialogCommand.Execute(ShowCheckbox ? DontShowAgain : null, null);
        }

        public void OnNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo() {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
    }
}
