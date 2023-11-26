using Stylet;

namespace MultiFunPlayer.UI.Dialogs.ViewModels;

internal sealed class TextInputMessageDialogViewModel(string label, string initialValue = null) : Screen
{
    public string Label { get; } = label;
    public string Value { get; set; } = initialValue;
}
