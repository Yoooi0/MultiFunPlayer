using MaterialDesignColors.ColorManipulation;
using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using Stylet;
using System.Windows.Media;

namespace MultiFunPlayer.UI.Controls.ViewModels;

public class ThemeViewModel : Screen, IHandle<AppSettingsMessage>, IDisposable
{
    private readonly AsyncManualResetEvent _refreshEvent;
    private readonly PaletteHelper _paletteHelper;
    private Task _task;
    private CancellationTokenSource _cancellationSource;

    public Color PrimaryColor { get; set; } = Color.FromRgb(0x71, 0x87, 0x92);
    public bool EnableColorAdjustment { get; set; } = false;
    public Contrast Contrast { get; set; } = Contrast.Medium;
    public double ContrastRatio { get; set; } = 4.5;

    public ThemeViewModel(IEventAggregator eventAggregator)
    {
        eventAggregator.Subscribe(this);
        _refreshEvent = new AsyncManualResetEvent();
        _paletteHelper = new PaletteHelper();

        if (_paletteHelper.GetTheme() is Theme theme)
        {
            PrimaryColor = theme.PrimaryDark.Color.Lighten();

            var colorAdjustment = new ColorAdjustment();
            Contrast = colorAdjustment.Contrast;
            ContrastRatio = colorAdjustment.DesiredContrastRatio;
        }

        _refreshEvent.Set();
        _cancellationSource = new CancellationTokenSource();
        _task = Task.Factory.StartNew(() => RunAsync(_cancellationSource.Token),
            _cancellationSource.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)
            .Unwrap();
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await _refreshEvent.WaitAsync(token);
                _refreshEvent.Reset();
                token.ThrowIfCancellationRequested();

                await Task.Delay(100, token);
                if (_refreshEvent.IsSet)
                    continue;

                if (_paletteHelper.GetTheme() is not Theme theme)
                    continue;

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
                _paletteHelper.SetTheme(theme);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void OnEnableColorAdjustmentChanged() => _refreshEvent.Set();
    public void OnPrimaryColorChanged() => _refreshEvent.Set();
    public void OnContrastChanged() => _refreshEvent.Set();
    public void OnContrastRatioChanged() => _refreshEvent.Set();

    public void Handle(AppSettingsMessage message)
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
        }
        else if (message.Action == SettingsAction.Loading)
        {
            if (!message.Settings.TryGetObject(out var settings, "Theme"))
                return;

            if (settings.TryGetValue<bool>(nameof(EnableColorAdjustment), out var enableColorAdjustment))
                EnableColorAdjustment = enableColorAdjustment;
            if (settings.TryGetValue<Color>(nameof(PrimaryColor), out var color))
                PrimaryColor = color;
            if (settings.TryGetValue<Contrast>(nameof(Contrast), out var contrast))
                Contrast = contrast;
            if (settings.TryGetValue<double>(nameof(ContrastRatio), out var contrastRatio))
                ContrastRatio = contrastRatio;
        }
    }

    protected virtual async void Dispose(bool disposing)
    {
        _cancellationSource?.Cancel();
        if (_task != null)
            await _task;
        _cancellationSource?.Dispose();

        _cancellationSource = null;
        _task = null;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}