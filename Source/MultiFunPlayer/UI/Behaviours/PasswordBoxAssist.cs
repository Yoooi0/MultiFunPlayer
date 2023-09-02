using System.Windows;
using System.Windows.Controls;

namespace MultiFunPlayer.UI.Behaviours;

public static class PasswordBoxAssist
{
    private static bool _eventAttached;
    private static bool _ignorePasswordChanged;

    public static readonly DependencyProperty PasswordProperty =
        DependencyProperty.RegisterAttached("Password",
            typeof(string), typeof(PasswordBoxAssist),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnPasswordPropertyChanged, OnCoercePasswordPropertyChanged));

    private static object OnCoercePasswordPropertyChanged(DependencyObject dp, object baseValue)
    {
        if (dp is PasswordBox passwordBox && !_eventAttached)
            AttachEvent(passwordBox);

        return baseValue;
    }

    public static string GetPassword(DependencyObject dp)
        => (string)dp.GetValue(PasswordProperty);

    public static void SetPassword(DependencyObject dp, string value)
        => dp.SetValue(PasswordProperty, value);

    private static void OnPasswordPropertyChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
    {
        if (_ignorePasswordChanged)
            return;

        if (dp is not PasswordBox passwordBox)
            return;

        if (!_eventAttached)
            AttachEvent(passwordBox);

        if (e.NewValue is not string value)
            return;

        passwordBox.Password = value;
    }

    private static void AttachEvent(PasswordBox passwordBox)
    {
        passwordBox.PasswordChanged -= OnPasswordChanged;
        passwordBox.PasswordChanged += OnPasswordChanged;
        _eventAttached = true;
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox passwordBox)
            return;

        _ignorePasswordChanged = true;
        SetPassword(sender as DependencyObject, passwordBox.Password);
        _ignorePasswordChanged = false;
    }
}