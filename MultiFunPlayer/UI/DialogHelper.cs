using MaterialDesignThemes.Wpf;
using MultiFunPlayer.UI.Controls.ViewModels;
using Stylet;
using System.Windows;

namespace MultiFunPlayer.UI;

public class DialogHelper
{
    private static DialogHelper Instance { get; set; }
    private ApplicationViewModel Application { get; }
    private IViewManager ViewManager { get; }


    public DialogHelper(ApplicationViewModel application, IViewManager viewManager)
    {
        Instance = this;
        Application = application;
        ViewManager = viewManager;
    }

    private static bool CanShowError => Instance?.Application?.ShowErrorDialogs ?? true;

    public static Task ShowErrorAsync(string message, string dialogName)
        => CanShowError ? ShowOnUIThreadAsync(new ErrorMessageDialogViewModel(message), dialogName) : Task.CompletedTask;

    public static Task ShowErrorAsync(Exception exception, string message, string dialogName)
        => CanShowError ? ShowOnUIThreadAsync(new ErrorMessageDialogViewModel(exception, message), dialogName) : Task.CompletedTask;

    public static Task ShowOnUIThreadAsync(object model, string dialogName)
        => Execute.OnUIThreadAsync(async () => await ShowAsync(model, dialogName));

    public static async Task<object> ShowAsync(object model, string dialogName)
    {
        var view = Instance.ViewManager.CreateAndBindViewForModelIfNecessary(model);
        var session = DialogHost.GetDialogSession(dialogName);
        var sessionContext = (session?.Content as FrameworkElement)?.DataContext;
        if (model.Equals(sessionContext))
            return null;

        if (DialogHost.IsDialogOpen(dialogName))
            DialogHost.Close(dialogName);

        (model as IScreenState)?.Activate();
        var result = await DialogHost.Show(view, dialogName).ConfigureAwait(true);
        (model as IScreenState)?.Deactivate();
        return result;
    }
}
