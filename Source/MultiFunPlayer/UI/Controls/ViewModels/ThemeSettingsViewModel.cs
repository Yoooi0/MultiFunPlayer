using MaterialDesignColors.ColorManipulation;
using MaterialDesignThemes.MahApps;
using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using Stylet;
using System.Windows;
using System.Windows.Media;

namespace MultiFunPlayer.UI.Controls.ViewModels;

internal sealed class ThemeSettingsViewModel : Screen, IHandle<SettingsMessage>
{
    private readonly PaletteHelper _paletteHelper;

    private bool _ignorePropertyChanged;

    public Color PrimaryColor { get; set; } = Color.FromRgb(0x71, 0x87, 0x92);
    public bool EnableColorAdjustment { get; set; } = false;
    public Contrast Contrast { get; set; } = Contrast.Medium;
    public double ContrastRatio { get; set; } = 4.5;
    public bool IsDarkTheme { get; set; } = false;

    public ThemeSettingsViewModel(IEventAggregator eventAggregator)
    {
        DisplayName = "Theme";
        eventAggregator.Subscribe(this);
        _paletteHelper = new PaletteHelper();
    }

    protected override void OnPropertyChanged(string propertyName)
    {
        base.OnPropertyChanged(propertyName);

        if (_ignorePropertyChanged)
            return;

        if (propertyName is nameof(EnableColorAdjustment) or nameof(PrimaryColor)
                         or nameof(Contrast) or nameof(ContrastRatio) or nameof(IsDarkTheme))
            ApplyTheme();
    }

    public void OnResetClick()
    {
        IgnorePropertyChanged(() =>
        {
            PrimaryColor = Color.FromRgb(0x71, 0x87, 0x92);
            EnableColorAdjustment = false;
            Contrast = Contrast.Medium;
            ContrastRatio = 4.5;
            IsDarkTheme = false;
        });
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        if (_paletteHelper.GetTheme() is not Theme theme)
            return;

        theme.SetBaseTheme(IsDarkTheme ? Theme.Dark : Theme.Light);

        if (EnableColorAdjustment)
            theme.ColorAdjustment ??= new ColorAdjustment();
        else
            theme.ColorAdjustment = null;

        if (theme.ColorAdjustment is ColorAdjustment colorAdjustment)
        {
            colorAdjustment.DesiredContrastRatio = (float)ContrastRatio;
            colorAdjustment.Contrast = Contrast;
        }

        theme.SetPrimaryColor(PrimaryColor);
        theme.SetSecondaryColor(PrimaryColor);

        _paletteHelper.SetTheme(theme);
        Application.Current.Resources.SetMahApps(theme, IsDarkTheme ? BaseTheme.Dark : BaseTheme.Light);

        var customThemeSource = $"pack://application:,,,/UI/Themes/Color.{(IsDarkTheme ? "Dark" : "Light")}.xaml";
        var customThemeResource = new ResourceDictionary { Source = new Uri(customThemeSource) };

        AutoUpdateSolidColorBrush("MaterialDesignErrorBrush");
        AutoUpdateSolidColorBrush("MaterialDesignPendingBrush");
        AutoUpdateSolidColorBrush("MaterialDesignWarningBrush");
        AutoUpdateSolidColorBrush("MaterialDesignSuccessBrush");

        AutoUpdateSolidColorBrush("MaterialDesignLightErrorBrush");
        AutoUpdateSolidColorBrush("MaterialDesignLightPendingBrush");
        AutoUpdateSolidColorBrush("MaterialDesignLightWarningBrush");
        AutoUpdateSolidColorBrush("MaterialDesignLightSuccessBrush");

        AutoUpdateSolidColorBrush("MaterialDesignPrimaryCheckerboxBrush");
        AutoUpdateSolidColorBrush("MaterialDesignSecondaryCheckerboxBrush");

        AutoUpdateSolidColorBrush("MaterialDesignBodyDisabledBrush");

        AutoUpdateSolidColorBrush("MaterialDesignCardBackgroundHoverBrush");
        AutoUpdateSolidColorBrush("MaterialDesignCardBackgroundSelectedBrush");

        var invertedLight = IsDarkTheme ? theme.PrimaryDark : theme.PrimaryLight;
        var invertedDark = IsDarkTheme ? theme.PrimaryLight : theme.PrimaryDark;
        UpdateSolidColorBrush("InvertedPrimaryHueLightBrush", invertedLight.Color);
        UpdateSolidColorBrush("InvertedPrimaryHueLightForegroundBrush", invertedLight.ForegroundColor ?? invertedLight.Color.ContrastingForegroundColor());
        UpdateSolidColorBrush("InvertedPrimaryHueDarkBrush", invertedDark.Color);
        UpdateSolidColorBrush("InvertedPrimaryHueDarkForegroundBrush", invertedDark.ForegroundColor ?? invertedDark.Color.ContrastingForegroundColor());

        void AutoUpdateSolidColorBrush(string brushName)
        {
            var color = (Color)customThemeResource[brushName.Replace("Brush", "Color")];
            UpdateSolidColorBrush(brushName, color);
        }

        void UpdateSolidColorBrush(string brushName, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Application.Current.Resources[brushName] = brush;
        }
    }

    public void Handle(SettingsMessage message)
    {
        if (message.Action == SettingsAction.Saving)
        {
            if (!message.Settings.EnsureContainsObjects("Theme")
             || !message.Settings.TryGetObject(out var settings, "Theme"))
                return;

            settings[nameof(EnableColorAdjustment)] = EnableColorAdjustment;
            settings[nameof(PrimaryColor)] = JToken.FromObject(PrimaryColor);
            settings[nameof(Contrast)] = JToken.FromObject(Contrast);
            settings[nameof(ContrastRatio)] = ContrastRatio;
            settings[nameof(IsDarkTheme)] = IsDarkTheme;
        }
        else if (message.Action == SettingsAction.Loading)
        {
            if (!message.Settings.TryGetObject(out var settings, "Theme"))
                return;

            IgnorePropertyChanged(() =>
            {
                if (settings.TryGetValue<bool>(nameof(EnableColorAdjustment), out var enableColorAdjustment))
                    EnableColorAdjustment = enableColorAdjustment;
                if (settings.TryGetValue<Color>(nameof(PrimaryColor), out var color))
                    PrimaryColor = color;
                if (settings.TryGetValue<Contrast>(nameof(Contrast), out var contrast))
                    Contrast = contrast;
                if (settings.TryGetValue<double>(nameof(ContrastRatio), out var contrastRatio))
                    ContrastRatio = contrastRatio;
                if (settings.TryGetValue<bool>(nameof(IsDarkTheme), out var isDarkTheme))
                    IsDarkTheme = isDarkTheme;
            });
            ApplyTheme();
        }
    }

    private void IgnorePropertyChanged(Action action)
    {
        _ignorePropertyChanged = true;
        action();
        _ignorePropertyChanged = false;
    }
}