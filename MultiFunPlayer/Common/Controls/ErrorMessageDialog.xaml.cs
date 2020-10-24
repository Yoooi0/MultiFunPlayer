using System.Windows.Controls;

namespace MultiFunPlayer.Common.Controls
{
    /// <summary>
    /// Interaction logic for ErrorMessageDialog.xaml
    /// </summary>
    public partial class ErrorMessageDialog : UserControl
    {
        public ErrorMessageDialog(string message)
        {
            InitializeComponent();
            Message.Text = message;
        }
    }
}
