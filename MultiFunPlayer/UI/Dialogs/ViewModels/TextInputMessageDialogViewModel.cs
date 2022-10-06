namespace MultiFunPlayer.UI.Dialogs.ViewModels;

public class TextInputMessageDialogViewModel
{
    public string Label { get; }
    public string Value { get; set; }

    public TextInputMessageDialogViewModel(string label, string initialValue = null)
    {
        Label = label;
        Value = initialValue;
    }
}
