using Microsoft.WindowsAPICodePack.Dialogs;
using MultiFunPlayer.ViewModels;
using Stylet;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MultiFunPlayer.Common.Controls
{
    /// <summary>
    /// Interaction logic for ScriptLibrariesDialog.xaml
    /// </summary>
    public partial class ScriptLibrariesDialog : UserControl
    {
        public BindableCollection<ScriptLibrary> Libraries { get; }

        public ScriptLibrariesDialog(BindableCollection<ScriptLibrary> libraries)
        {
            Libraries = libraries;

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
            if (Libraries.Any(x => string.Equals(x.Directory.FullName, directory.FullName)))
                return;

            Libraries.Add(new ScriptLibrary(directory));
        }

        public void OnDelete(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not ScriptLibrary library)
                return;

            Libraries.Remove(library);
        }

        public void OnOpenFolder(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not ScriptLibrary library)
                return;

            Process.Start("explorer.exe", library.Directory.FullName);
        }
    }
}
