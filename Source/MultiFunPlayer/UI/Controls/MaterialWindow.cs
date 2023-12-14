using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MultiFunPlayer.UI.Controls;

[TemplatePart(Name = "PART_MinimizeButton", Type = typeof(Button))]
[TemplatePart(Name = "PART_MaximizeRestoreButton", Type = typeof(Button))]
[TemplatePart(Name = "PART_CloseButton", Type = typeof(Button))]
public class MaterialWindow : Window
{
    public static readonly DependencyProperty BorderBackgroundBrushProperty =
        DependencyProperty.Register(nameof(BorderBackgroundBrush),
            typeof(Brush), typeof(MaterialWindow),
                new FrameworkPropertyMetadata(null, null));

    public Brush BorderBackgroundBrush
    {
        get => (Brush)GetValue(BorderBackgroundBrushProperty);
        set => SetValue(BorderBackgroundBrushProperty, value);
    }

    public static readonly DependencyProperty BorderForegroundBrushProperty =
        DependencyProperty.Register(nameof(BorderForegroundBrush),
            typeof(Brush), typeof(MaterialWindow),
            new FrameworkPropertyMetadata(null, null));

    public Brush BorderForegroundBrush
    {
        get => (Brush)GetValue(BorderForegroundBrushProperty);
        set => SetValue(BorderForegroundBrushProperty, value);
    }

    public static readonly DependencyProperty FadeContentIfInactiveProperty =
        DependencyProperty.Register(nameof(FadeContentIfInactive),
            typeof(bool), typeof(MaterialWindow),
                new FrameworkPropertyMetadata(true));

    public bool FadeContentIfInactive
    {
        get => (bool)GetValue(FadeContentIfInactiveProperty);
        set => SetValue(FadeContentIfInactiveProperty, value);
    }

    public static readonly DependencyProperty TitleTemplateProperty =
        DependencyProperty.Register(nameof(TitleTemplate),
            typeof(DataTemplate), typeof(MaterialWindow));

    public DataTemplate TitleTemplate
    {
        get => (DataTemplate)GetValue(TitleTemplateProperty);
        set => SetValue(TitleTemplateProperty, value);
    }

    public static readonly DependencyProperty TitleBarIconProperty =
        DependencyProperty.Register(nameof(TitleBarIcon),
            typeof(ImageSource), typeof(MaterialWindow),
                new FrameworkPropertyMetadata(null, null));

    public ImageSource TitleBarIcon
    {
        get => (ImageSource)GetValue(TitleBarIconProperty);
        set => SetValue(TitleBarIconProperty, value);
    }

    private Button _minimizeButton;
    private Button _maximizeRestoreButton;
    private Button _closeButton;

    static MaterialWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(MaterialWindow), new FrameworkPropertyMetadata(typeof(MaterialWindow)));
    }

    public override void OnApplyTemplate()
    {
        if (_minimizeButton != null)
            _minimizeButton.Click -= MinimizeButtonClickHandler;

        if (_closeButton != null)
            _closeButton.Click -= CloseButtonClickHandler;

        if (_maximizeRestoreButton != null)
            _maximizeRestoreButton.Click -= MaximizeRestoreButtonClickHandler;

        _minimizeButton = GetTemplateChild("PART_MinimizeButton") as Button;
        _maximizeRestoreButton = GetTemplateChild("PART_MaximizeRestoreButton") as Button;
        _closeButton = GetTemplateChild("PART_CloseButton") as Button;

        if (_minimizeButton != null)
            _minimizeButton.Click += MinimizeButtonClickHandler;

        if (_maximizeRestoreButton != null)
            _maximizeRestoreButton.Click += MaximizeRestoreButtonClickHandler;

        if (_closeButton != null)
            _closeButton.Click += CloseButtonClickHandler;

        base.OnApplyTemplate();
    }

    private void CloseButtonClickHandler(object sender, RoutedEventArgs args) => Close();

    private void MaximizeRestoreButtonClickHandler(object sender, RoutedEventArgs args)
        => WindowState = (WindowState == WindowState.Normal) ? WindowState.Maximized : WindowState.Normal;

    private void MinimizeButtonClickHandler(object sender, RoutedEventArgs args)
        => WindowState = WindowState.Minimized;
}