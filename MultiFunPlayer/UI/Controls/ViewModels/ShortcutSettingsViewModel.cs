using MaterialDesignThemes.Wpf;
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
public class ShortcutSettingsViewModel : Screen, IHandle<AppSettingsMessage>, IDisposable
{
    protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly IShortcutManager _manager;
    private readonly Channel<IInputGesture> _captureGestureChannel;
    private CancellationTokenSource _captureGestureCancellationSource;

    public string ActionsFilter { get; set; }
    public ICollectionView AvailableActionsView { get; }
    public ObservableConcurrentCollection<IShortcutActionDescriptor> AvailableActions => _manager.AvailableActions;
    public ObservableConcurrentDictionary<IInputGestureDescriptor, ObservableConcurrentCollection<IShortcutAction>> Bindings => _manager.Bindings;

    public bool IsCapturingGesture { get; private set; }
    public IInputGestureDescriptor CapturedGesture { get; set; }
    public KeyValuePair<IInputGestureDescriptor, ObservableConcurrentCollection<IShortcutAction>>? SelectedBinding { get; set; }

    [JsonProperty] public bool IsKeyboardKeysGestureEnabled { get; set; } = true;
    [JsonProperty] public bool IsMouseAxisGestureEnabled { get; set; } = false;
    [JsonProperty] public bool IsMouseButtonGestureEnabled { get; set; } = false;
    [JsonProperty] public bool IsGamepadAxisGestureEnabled { get; set; } = true;
    [JsonProperty] public bool IsGamepadButtonGestureEnabled { get; set; } = true;

    public ShortcutSettingsViewModel(IShortcutManager manager, IEventAggregator eventAggregator)
    {
        DisplayName = "Shortcut";
        _manager = manager;
        Logger.Debug($"Initialized with {manager.AvailableActions.Count} available actions");

        eventAggregator.Subscribe(this);

        AvailableActionsView = CollectionViewSource.GetDefaultView(AvailableActions);
        AvailableActionsView.Filter = o =>
        {
            if (o is not IShortcutActionDescriptor actionDescriptor)
                return false;
            if (SelectedBinding == null)
                return false;

            var (selectedGesture, _) = SelectedBinding.Value;
            if (!actionDescriptor.AcceptsSimpleGesture && selectedGesture is ISimpleInputGestureDescriptor)
                return false;
            if (!actionDescriptor.AcceptsAxisGesture && selectedGesture is IAxisInputGestureDescriptor)
                return false;

            if (!string.IsNullOrWhiteSpace(ActionsFilter))
            {
                var filterWords = ActionsFilter.Split(' ');
                if (!filterWords.All(w => actionDescriptor.Name.Contains(w, StringComparison.InvariantCultureIgnoreCase)))
                    return false;
            }

            return true;
        };

        _manager.OnGesture += HandleGesture;
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

    protected override void OnActivate() => _manager.HandleGestures = false;
    protected override void OnDeactivate() => _manager.HandleGestures = true;

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

        _manager.RegisterGesture(CapturedGesture);
        SelectedBinding = KeyValuePair.Create(CapturedGesture, Bindings[CapturedGesture]);

        CapturedGesture = null;
    }

    public void RemoveGesture(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not KeyValuePair<IInputGestureDescriptor, ObservableConcurrentCollection<IShortcutAction>> pair)
            return;

        var (gestureDescriptor, _) = pair;
        _manager.UnregisterGesture(gestureDescriptor);
    }

    public void AssignAction(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IShortcutActionDescriptor actionDescriptor)
            return;
        if (SelectedBinding == null)
            return;

        var binding = SelectedBinding.Value;
        _manager.BindAction(binding.Key, actionDescriptor);
    }

    public void RemoveAssignedAction(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IShortcutAction action)
            return;
        if (SelectedBinding == null)
            return;

        var binding = SelectedBinding.Value;
        _manager.UnbindAction(binding.Key, action);
    }

    public void MoveAssignedActionUp(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IShortcutAction action)
            return;
        if (SelectedBinding == null)
            return;

        var binding = SelectedBinding.Value;
        var index = binding.Value.IndexOf(action);
        if (index == 0)
            return;

        binding.Value.Move(index, index - 1);
    }

    public void ConfigureAssignedAction(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IShortcutAction action)
            return;

        _ = DialogHelper.ShowOnUIThreadAsync(new ConfigureShortcutActionDialogViewModel(action), "SettingsDialog");
    }

    public void MoveAssignedActionDown(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IShortcutAction action)
            return;
        if (SelectedBinding == null)
            return;

        var binding = SelectedBinding.Value;
        var index = binding.Value.IndexOf(action);
        if (index == binding.Value.Count - 1)
            return;

        binding.Value.Move(index, index + 1);
    }

    private async Task TryCaptureGestureAsync(CancellationToken token)
    {
        bool ValidateGesture(IInputGesture gesture)
            => !_manager.Bindings.ContainsKey(gesture.Descriptor);

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

    public void Handle(AppSettingsMessage message)
    {
        if (message.Action == SettingsAction.Saving)
        {
            var settings = JObject.FromObject(this);
            settings[nameof(Bindings)] = JArray.FromObject(Bindings.Select(x => BindingSettingsModel.FromBinding(x)));

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

            if (settings.TryGetValue<List<BindingSettingsModel>>(nameof(Bindings), out var loadedBindings))
            {
                foreach (var gestureDescriptor in Bindings.Keys.ToList())
                    _manager.UnregisterGesture(gestureDescriptor);

                foreach (var binding in loadedBindings)
                {
                    var gestureDescriptor = binding.Gesture;
                    _manager.RegisterGesture(gestureDescriptor);

                    if (binding.Actions == null)
                        continue;

                    foreach (var action in binding.Actions)
                    {
                        var actionDescriptor = AvailableActions.FirstOrDefault(d => string.Equals(d.Name, action.Descriptor, StringComparison.OrdinalIgnoreCase));
                        if (actionDescriptor == null)
                            Logger.Warn($"Action \"{action.Descriptor}\" not found!");
                        else
                            _manager.BindActionWithSettings(gestureDescriptor, actionDescriptor, action.Values);
                    }
                }
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

public class BindingSettingsModel
{
    [JsonProperty(TypeNameHandling = TypeNameHandling.Objects)]
    public IInputGestureDescriptor Gesture { get; init; }
    public List<ActionSettingsModel> Actions { get; init; }

    public static BindingSettingsModel FromBinding(KeyValuePair<IInputGestureDescriptor, ObservableConcurrentCollection<IShortcutAction>> binding)
        => new()
        {
            Gesture = binding.Key,
            Actions = binding.Value.Select(x => new ActionSettingsModel()
            {
                Descriptor = x.Descriptor.Name,
                Values = x.Settings.Select(s => new TypedValue(s.GetType().GetGenericArguments()[0], s.Value)).ToList()
            }).ToList()
        };
}

public class ActionSettingsModel
{
    public string Descriptor { get; init; }

    [JsonProperty("Settings")]
    public List<TypedValue> Values { get; init; }
}