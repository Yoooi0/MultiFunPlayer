using MaterialDesignThemes.Wpf;
using MultiFunPlayer.UI.Controls.ViewModels;
using MultiFunPlayer.UI.Dialogs.ViewModels;
using Stylet;
using System.Windows;

namespace MultiFunPlayer.UI;

internal static class DialogHelper
{
    private static SettingsViewModel Settings { get; set; }
    private static IViewManager ViewManager { get; set; }

    private static bool CanShowError => Settings?.General?.ShowErrorDialogs ?? true;

    public static void Initialize(IViewManager viewManager, SettingsViewModel settings)
    {
        ViewManager = viewManager;
        Settings = settings;
    }

    public static Task ShowErrorAsync(string message, string dialogName)
        => CanShowError ? ShowOnUIThreadAsync(new ErrorMessageDialogViewModel(message), dialogName) : Task.CompletedTask;

    public static Task ShowErrorAsync(Exception exception, string message, string dialogName)
        => CanShowError ? ShowOnUIThreadAsync(new ErrorMessageDialogViewModel(exception, message), dialogName) : Task.CompletedTask;

    public static Task ShowOnUIThreadAsync(object model, string dialogName)
        => Execute.OnUIThreadAsync(async () => await ShowAsync(model, dialogName));

    public static async Task<object> ShowAsync(object model, string dialogName)
    {
        var view = ViewManager.CreateAndBindViewForModelIfNecessary(model);
        var session = DialogHost.GetDialogSession(dialogName);
        var sessionContext = (session?.Content as FrameworkElement)?.DataContext;
        if (model.Equals(sessionContext))
            return null;

        if (DialogHost.IsDialogOpen(dialogName))
            DialogHost.Close(dialogName);

        (model as IScreenState)?.Activate();
        var result = await DialogHost.Show(view, dialogName);
        (model as IScreenState)?.Deactivate();
        return result;
    }
}
