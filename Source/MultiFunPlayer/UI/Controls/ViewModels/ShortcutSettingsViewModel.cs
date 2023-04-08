using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.Input.RawInput;
using MultiFunPlayer.Input.XInput;
using MultiFunPlayer.Settings;
using MultiFunPlayer.UI.Dialogs.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Controls.ViewModels;

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
internal class ShortcutSettingsViewModel : Screen, IHandle<SettingsMessage>, IDisposable
{
    protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly IShortcutManager _manager;
    private readonly IShortcutBinder _binder;
    private readonly Channel<IInputGesture> _captureGestureChannel;
    private CancellationTokenSource _captureGestureCancellationSource;

    public string ActionsFilter { get; set; }
    public ICollectionView AvailableActionsView { get; }
    public IReadOnlyConcurrentObservableCollection<IShortcutActionDescriptor> AvailableActions => _manager.AvailableActions;
    public IReadOnlyConcurrentObservableCollection<IShortcutBinding> Bindings => _binder.Bindings;

    public bool IsCapturingGesture { get; private set; }
    public IInputGestureDescriptor CapturedGesture { get; set; }
    public IShortcutBinding SelectedBinding { get; set; }

    [JsonProperty] public bool IsKeyboardKeysGestureEnabled { get; set; } = true;
    [JsonProperty] public bool IsMouseAxisGestureEnabled { get; set; } = false;
    [JsonProperty] public bool IsMouseButtonGestureEnabled { get; set; } = false;
    [JsonProperty] public bool IsGamepadAxisGestureEnabled { get; set; } = true;
    [JsonProperty] public bool IsGamepadButtonGestureEnabled { get; set; } = true;

    public ShortcutSettingsViewModel(IShortcutManager manager, IShortcutBinder binder, IEventAggregator eventAggregator)
    {
        DisplayName = "Shortcut";
        _manager = manager;
        _binder = binder;

        Logger.Debug($"Initialized with {manager.AvailableActions.Count} available actions");

        eventAggregator.Subscribe(this);

        AvailableActionsView = CollectionViewSource.GetDefaultView(AvailableActions);
        AvailableActionsView.Filter = o =>
        {
            if (o is not IShortcutActionDescriptor actionDescriptor)
                return false;
            if (SelectedBinding == null)
                return false;

            if (!_manager.ActionAcceptsGesture(actionDescriptor, SelectedBinding.Gesture))
                return false;

            if (!string.IsNullOrWhiteSpace(ActionsFilter))
            {
                var filterWords = ActionsFilter.Split(' ');
                if (!filterWords.All(w => actionDescriptor.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }

            return true;
        };

        _binder.OnGesture += HandleGesture;
        _captureGestureChannel = Channel.CreateBounded<IInputGesture>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ActionsFilter) || e.PropertyName == nameof(SelectedBinding))
                AvailableActionsView.Refresh();
        };
    }

    protected override void OnActivate() => _binder.HandleGestures = false;
    protected override void OnDeactivate() => _binder.HandleGestures = true;

    private async void HandleGesture(object sender, GestureEventArgs e)
    {
        if (!IsCapturingGesture)
            return;

        switch (e.Gesture)
        {
            case KeyboardGesture when !IsKeyboardKeysGestureEnabled:
            case MouseAxisGesture when !IsMouseAxisGestureEnabled:
            case MouseButtonGesture when !IsMouseButtonGestureEnabled:
            case GamepadAxisGesture when !IsGamepadAxisGestureEnabled:
            case GamepadButtonGesture when !IsGamepadButtonGestureEnabled:
            case IAxisInputGesture axisGesture when Math.Abs(axisGesture.Delta) < 0.01:
                return;
        }

        await _captureGestureChannel.Writer.WriteAsync(e.Gesture);
    }

    public async void CaptureGesture(object sender, RoutedEventArgs e)
    {
        if (IsCapturingGesture)
            return;

        if (!IsKeyboardKeysGestureEnabled && !IsMouseAxisGestureEnabled
        && !IsMouseButtonGestureEnabled && !IsGamepadAxisGestureEnabled
        && !IsGamepadButtonGestureEnabled)
            return;

        _captureGestureCancellationSource = new CancellationTokenSource();
        await TryCaptureGestureAsync(_captureGestureCancellationSource.Token).ConfigureAwait(true);
        _captureGestureCancellationSource.Dispose();
        _captureGestureCancellationSource = null;
    }

    public void AddGesture(object sender, RoutedEventArgs e)
    {
        if (_captureGestureCancellationSource?.IsCancellationRequested == false)
            _captureGestureCancellationSource?.Cancel();

        if (CapturedGesture == null)
            return;

        SelectedBinding = _binder.GetOrCreateBinding(CapturedGesture);
        CapturedGesture = null;
    }

    public void RemoveGesture(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IShortcutBinding binding)
            return;

        _binder.RemoveBinding(binding);
    }

    public void AssignAction(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IShortcutActionDescriptor actionDescriptor)
            return;
        if (SelectedBinding == null)
            return;

        _binder.BindAction(SelectedBinding.Gesture, actionDescriptor);
    }

    public void RemoveAssignedAction(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IShortcutActionConfiguration configuration)
            return;
        if (SelectedBinding == null)
            return;

        _binder.UnbindAction(SelectedBinding.Gesture, configuration);
    }

    public void MoveAssignedActionUp(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IShortcutActionConfiguration configuration)
            return;
        if (SelectedBinding == null)
            return;

        var configurations = SelectedBinding.Configurations;
        var index = configurations.IndexOf(configuration);
        if (index == 0)
            return;

        configurations.Move(index, index - 1);
    }

    public void ConfigureAssignedAction(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IShortcutActionConfiguration configuration)
            return;

        _ = DialogHelper.ShowOnUIThreadAsync(new ShortcutActionConfigurationDialogViewModel(configuration), "SettingsDialog");
    }

    public void MoveAssignedActionDown(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IShortcutActionConfiguration configuration)
            return;
        if (SelectedBinding == null)
            return;

        var configurations = SelectedBinding.Configurations;
        var index = configurations.IndexOf(configuration);
        if (index == configurations.Count - 1)
            return;

        configurations.Move(index, index + 1);
    }

    private async Task TryCaptureGestureAsync(CancellationToken token)
    {
        bool ValidateGesture(IInputGesture gesture)
            => !_binder.ContainsBinding(gesture.Descriptor);

        var tryCount = 0;
        var gesture = default(IInputGesture);

        while (_captureGestureChannel.Reader.TryRead(out var _));

        IsCapturingGesture = true;

        try
        {
            do
            {
                _ = await _captureGestureChannel.Reader.WaitToReadAsync(token).ConfigureAwait(true);
                gesture = await _captureGestureChannel.Reader.ReadAsync(token).ConfigureAwait(true);
            } while (!token.IsCancellationRequested && !ValidateGesture(gesture) && tryCount++ < 5);
        }
        catch (OperationCanceledException) { }

        IsCapturingGesture = false;
        if (token.IsCancellationRequested || tryCount >= 5)
            gesture = null;

        CapturedGesture = gesture?.Descriptor;
    }

    public void Handle(SettingsMessage message)
    {
        if (message.Action == SettingsAction.Saving)
        {
            var settings = JObject.FromObject(this);
            settings[nameof(Bindings)] = JArray.FromObject(Bindings);

            message.Settings["Shortcuts"] = settings;
        }
        else if (message.Action == SettingsAction.Loading)
        {
            if (!message.Settings.TryGetObject(out var settings, "Shortcuts"))
                return;

            if (settings.TryGetValue<bool>(nameof(IsKeyboardKeysGestureEnabled), out var isKeyboardKeysGestureEnabled))
                IsKeyboardKeysGestureEnabled = isKeyboardKeysGestureEnabled;
            if (settings.TryGetValue<bool>(nameof(IsMouseAxisGestureEnabled), out var isMouseAxisGestureEnabled))
                IsMouseAxisGestureEnabled = isMouseAxisGestureEnabled;
            if (settings.TryGetValue<bool>(nameof(IsMouseButtonGestureEnabled), out var isMouseButtonGestureEnabled))
                IsMouseButtonGestureEnabled = isMouseButtonGestureEnabled;
            if (settings.TryGetValue<bool>(nameof(IsGamepadAxisGestureEnabled), out var isHidAxisGestureEnabled))
                IsGamepadAxisGestureEnabled = isHidAxisGestureEnabled;
            if (settings.TryGetValue<bool>(nameof(IsGamepadButtonGestureEnabled), out var isHidButtonGestureEnabled))
                IsGamepadButtonGestureEnabled = isHidButtonGestureEnabled;

            if (settings.TryGetValue<List<ShortcutBinding>>(nameof(Bindings), out var bindings))
            {
                _binder.Clear();
                foreach (var binding in bindings)
                    _binder.AddBinding(binding);
            }
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        _manager?.Dispose();
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}