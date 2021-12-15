using System.Windows;

namespace MultiFunPlayer.UI;

public class BindingProxy : Freezable
{
    protected override Freezable CreateInstanceCore() => new BindingProxy();

    public static readonly DependencyProperty DataContextProperty =
        DependencyProperty.Register(nameof(DataContext),
            typeof(object), typeof(BindingProxy),
                new UIPropertyMetadata(null));

    public object DataContext
    {
        get { return GetValue(DataContextProperty); }
        set { SetValue(DataContextProperty, value); }
    }
}
