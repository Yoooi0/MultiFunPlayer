using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MultiFunPlayer.UI.Controls;

internal sealed class AutoClosingPopup : Popup
{
    public AutoClosingPopup() => IsHitTestVisible = true;

    protected override void OnMouseEnter(MouseEventArgs e) => Close(e);
    protected override void OnMouseLeave(MouseEventArgs e) => Close(e);
    protected override void OnMouseMove(MouseEventArgs e) => Close(e);
    protected override void OnPreviewMouseMove(MouseEventArgs e) => Close(e);

    private void Close(MouseEventArgs e)
    {
        e.Handled = true;
        IsOpen = false;
    }
}
