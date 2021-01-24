using Microsoft.WindowsAPICodePack.Dialogs;
using Stylet;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MultiFunPlayer.Common.Controls
{
    /// <summary>
    /// Interaction logic for ScriptDirectoriesDialog.xaml
    /// </summary>
    public partial class ScriptDirectoriesDialog : UserControl
    {
        public BindableCollection<DirectoryInfo> Directories { get; }

        public ScriptDirectoriesDialog(BindableCollection<DirectoryInfo> directories)
        {
            Directories = directories;

            InitializeComponent();
        }

        public void OnAdd(object sender, RoutedEventArgs e)
        {
            //TODO: remove dependency once /dotnet/wpf/issues/438 is resolved
            var dialog = new CommonOpenFileDialog()
            {
                IsFolderPicker = true
            };

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
                return;

            var directory = new DirectoryInfo(dialog.FileName);
            if (Directories.Any(x => string.Equals(x.FullName, directory.FullName)))
                return;

            Directories.Add(directory);
        }

        public void OnDelete(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not DirectoryInfo directory)
                return;

            Directories.Remove(directory);
        }

        public void OnOpenFolder(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not DirectoryInfo directory)
                return;

            Process.Start("explorer.exe", directory.FullName);
        }
    }
}
