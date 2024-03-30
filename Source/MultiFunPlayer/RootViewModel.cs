using MultiFunPlayer.Common;
using MultiFunPlayer.UI;
using MultiFunPlayer.UI.Controls.ViewModels;
using Newtonsoft.Json.Linq;
using Stylet;
using StyletIoC;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace MultiFunPlayer;

internal sealed class RootViewModel : Conductor<IScreen>.Collection.AllActive, IHandle<SettingsMessage>
{
    private bool _isDragging;
    private double _lastValidWindowLeft;
    private double _lastValidWindowTop;

    [Inject] public ScriptViewModel Script { get; set; }
    [Inject] public MediaSourceViewModel MediaSource { get; set; }
    [Inject] public OutputTargetViewModel OutputTarget { get; set; }
    [Inject] public SettingsViewModel Settings { get; set; }
    [Inject] public PluginViewModel Plugin { get; set; }
    [Inject] public InformationViewModel Information { get; set; }

    public bool DisablePopup { get; set; }

    public double WindowWidth { get; } = 600;
    public double WindowHeight { get; set; }
    public double WindowLeft { get; set; }
    public double WindowTop { get; set; }

    public string WindowTitleVersion => GitVersionInformation.CommitsSinceVersionSource == "0" ? $"v{GitVersionInformation.MajorMinorPatch}"
                                                                                               : $"v{GitVersionInformation.MajorMinorPatch}.{GitVersionInformation.ShortSha}";

    public RootViewModel(IEventAggregator eventAggregator)
    {
        eventAggregator.Subscribe(this);
        _lastValidWindowLeft = double.NaN;
        _lastValidWindowTop = double.NaN;
    }

    protected override void OnActivate()
    {
        Items.Add(Script);
        Items.Add(MediaSource);
        Items.Add(OutputTarget);
        Items.Add(Settings);
        Items.Add(Plugin);

        ActivateAndSetParent(Items);
        base.OnActivate();
    }

    public void OnInformationClick() => _ = DialogHelper.ShowOnUIThreadAsync(Information, "RootDialog");
    public void OnSettingsClick() => _ = DialogHelper.ShowOnUIThreadAsync(Settings, "RootDialog");
    public void OnPluginClick() => _ = DialogHelper.ShowOnUIThreadAsync(Plugin, "RootDialog");

    protected override void OnViewLoaded()
    {
        var window = Application.Current.MainWindow;
        if (window == null)
            return;

        window.WindowStartupLocation = Settings.General.RememberWindowLocation ? WindowStartupLocation.Manual
                                                                               : WindowStartupLocation.CenterScreen;

        var source = PresentationSource.FromVisual(window) as HwndSource;
        source.AddHook(MessageSink);
    }

    public void OnWindowLeftChanged()
    {
        if (WindowLeft != -32000)
            _lastValidWindowLeft = WindowLeft;
    }

    public void OnWindowTopChanged()
    {
        if (WindowTop != -32000)
            _lastValidWindowTop = WindowTop;
    }

    public void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        BringWindowIntoView(0.5);
    }

    public void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Window window)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        _isDragging = true;
        window.DragMove();
    }

    public void Handle(SettingsMessage message)
    {
        var settings = message.Settings;

        if (message.Action == SettingsAction.Saving)
        {
            settings[nameof(DisablePopup)] = DisablePopup;
            settings[nameof(WindowHeight)] = WindowHeight;
            settings[nameof(WindowLeft)] = _lastValidWindowLeft;
            settings[nameof(WindowTop)] = _lastValidWindowTop;
        }
        else if (message.Action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<double>(nameof(WindowHeight), out var windowHeight))
                WindowHeight = windowHeight;
            if (settings.TryGetValue<double>(nameof(WindowLeft), out var windowLeft))
                WindowLeft = windowLeft;
            if (settings.TryGetValue<double>(nameof(WindowTop), out var windowTop))
                WindowTop = windowTop;

            BringWindowIntoView(0);

            DisablePopup = settings.TryGetValue(nameof(DisablePopup), out var disablePopupToken) && disablePopupToken.Value<bool>();
            if (!DisablePopup)
            {
                Execute.PostToUIThread(async () =>
                {
                    await DialogHelper.ShowAsync(Information, "RootDialog");
                    DisablePopup = true;
                });
            }
        }
    }

    private void BringWindowIntoView(double windowSizeScale)
    {
        var virtualScreenLeft = SystemParameters.VirtualScreenLeft - WindowWidth * windowSizeScale;
        var virtualScreenTop = SystemParameters.VirtualScreenTop - WindowHeight * windowSizeScale;
        var virtualScreenRight = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth + WindowWidth * windowSizeScale;
        var virtualScreenBottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight + WindowHeight * windowSizeScale;

        if (WindowLeft < virtualScreenLeft) WindowLeft = virtualScreenLeft;
        if (WindowTop < virtualScreenTop) WindowTop = virtualScreenTop;
        if (WindowLeft + WindowWidth > virtualScreenRight) WindowLeft = virtualScreenRight - WindowWidth;
        if (WindowTop + WindowHeight > virtualScreenBottom) WindowTop = virtualScreenBottom - WindowHeight;
    }

    private IntPtr MessageSink(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_WINDOWPOSCHANGING = 0x0046;
        const uint SWP_NOMOVE = 0x0002;

        if (msg == WM_WINDOWPOSCHANGING && _isDragging && Mouse.LeftButton != MouseButtonState.Pressed)
        {
            var windowPos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if (WindowTop < 0 && windowPos.Y == 0)
            {
                windowPos.Flags |= SWP_NOMOVE;
                Marshal.StructureToPtr(windowPos, lParam, false);
            }
        }

        return IntPtr.Zero;
    }

    private record struct WINDOWPOS(IntPtr Hwnd, IntPtr HwndInsertAfter, int X, int Y, int CX, int CY, uint Flags);
}
