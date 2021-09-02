using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Input;
using MultiFunPlayer.Common.Input.RawInput;
using MultiFunPlayer.Common.Input.XInput;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace MultiFunPlayer.ViewModels
{
    public class ShortcutViewModel : Screen, IHandle<AppSettingsMessage>, IDisposable
    {
        protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly IShortcutManager _manager;
        private readonly Channel<IInputGesture> _captureGestureChannel;
        private CancellationTokenSource _captureGestureCancellationSource;

        public string ActionsFilter { get; set; }
        public BindableCollection<IShortcutActionDescriptor> ActionDescriptors { get; }
        public ICollectionView AvailableActionDescriptors { get; }
        public IReadOnlyDictionary<IInputGestureDescriptor, BindableCollection<IShortcutActionDescriptor>> Bindings => _manager.Bindings;

        public bool IsCapturingGesture { get; private set; }
        public IInputGestureDescriptor CapturedGesture { get; set; }
        public KeyValuePair<IInputGestureDescriptor, BindableCollection<IShortcutActionDescriptor>>? SelectedBinding { get; set; }

        public bool IsKeyboardKeysGestureEnabled { get; set; } = true;
        public bool IsMouseAxisGestureEnabled { get; set; } = false;
        public bool IsMouseButtonGestureEnabled { get; set; } = false;
        public bool IsGamepadAxisGestureEnabled { get; set; } = true;
        public bool IsGamepadButtonGestureEnabled { get; set; } = true;

        public ShortcutViewModel(IShortcutManager manager, IEventAggregator eventAggregator)
        {
            eventAggregator.Subscribe(this);

            Logger.Debug($"Found {manager.Actions.Count} available actions");

            ActionDescriptors = new BindableCollection<IShortcutActionDescriptor>(manager.Actions.Keys);
            AvailableActionDescriptors = CollectionViewSource.GetDefaultView(ActionDescriptors);
            AvailableActionDescriptors.Filter = o =>
            {
                if (o is not IShortcutActionDescriptor actionDescriptor)
                    return false;
                if (SelectedBinding == null)
                    return false;

                var (gestureDescriptor, _) = SelectedBinding.Value;
                if (gestureDescriptor is ISimpleInputGestureDescriptor && actionDescriptor is IAxisShortcutActionDescriptor)
                    return false;
                if (gestureDescriptor is IAxisInputGestureDescriptor && actionDescriptor is ISimpleShortcutActionDescriptor)
                    return false;

                if (!string.IsNullOrWhiteSpace(ActionsFilter))
                {
                    var filterWords = ActionsFilter.Split(' ');
                    if (!filterWords.All(w => actionDescriptor.Name.Contains(w, StringComparison.InvariantCultureIgnoreCase)))
                        return false;
                }

                return true;
            };

            _manager = manager;
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
                    AvailableActionDescriptors.Refresh();
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
                case IAxisInputGesture axisGesture when MathF.Abs(axisGesture.Delta) < 0.01f:
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
            CapturedGesture = null;
        }

        public void RemoveGesture(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not KeyValuePair<IInputGestureDescriptor, BindableCollection<IShortcutActionDescriptor>> pair)
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
            if (sender is not FrameworkElement element || element.DataContext is not IShortcutActionDescriptor actionDescriptor)
                return;
            if (SelectedBinding == null)
                return;

            var binding = SelectedBinding.Value;
            _manager.UnbindAction(binding.Key, actionDescriptor);
        }

        public void MoveAssignedActionUp(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not IShortcutActionDescriptor actionDescriptor)
                return;
            if (SelectedBinding == null)
                return;

            var binding = SelectedBinding.Value;
            var index = binding.Value.IndexOf(actionDescriptor);
            if (index == 0)
                return;

            binding.Value.Move(index, index - 1);
        }

        public void MoveAssignedActionDown(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not IShortcutActionDescriptor actionDescriptor)
                return;
            if (SelectedBinding == null)
                return;

            var binding = SelectedBinding.Value;
            var index = binding.Value.IndexOf(actionDescriptor);
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

            while (_captureGestureChannel.Reader.TryRead(out var _)) ;

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

        private class BindingConfigModel
        {
            [JsonProperty(TypeNameHandling = TypeNameHandling.Objects)]
            public IInputGestureDescriptor Gesture { get; }

            [JsonProperty(ItemTypeNameHandling = TypeNameHandling.Objects)]
            public List<IShortcutActionDescriptor> Actions { get; }

            public BindingConfigModel(IInputGestureDescriptor gesture, [JsonProperty(ItemTypeNameHandling = TypeNameHandling.Objects)] IEnumerable<IShortcutActionDescriptor> actions)
            {
                Gesture = gesture;
                Actions = actions?.ToList();
            }
        }

        public void Handle(AppSettingsMessage message)
        {
            if (message.Type == AppSettingsMessageType.Saving)
            {
                message.Settings["Shortcuts"] = new JObject
                {
                    { nameof(IsKeyboardKeysGestureEnabled), JValue.FromObject(IsKeyboardKeysGestureEnabled) },
                    { nameof(IsMouseAxisGestureEnabled), JValue.FromObject(IsMouseAxisGestureEnabled) },
                    { nameof(IsMouseButtonGestureEnabled), JValue.FromObject(IsMouseButtonGestureEnabled) },
                    { nameof(IsGamepadAxisGestureEnabled), JValue.FromObject(IsGamepadAxisGestureEnabled) },
                    { nameof(IsGamepadButtonGestureEnabled), JValue.FromObject(IsGamepadButtonGestureEnabled) },
                    { nameof(Bindings), JArray.FromObject(Bindings.Select(x => new BindingConfigModel(x.Key, x.Value)).ToList()) },
                };
            }
            else if (message.Type == AppSettingsMessageType.Loading)
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

                if (settings.TryGetValue<List<BindingConfigModel>>(nameof(Bindings), out var loadedBindings))
                {
                    foreach (var gestureDescriptor in Bindings.Keys.ToList())
                        _manager.UnregisterGesture(gestureDescriptor);

                    foreach (var binding in loadedBindings)
                    {
                        var gestureDescriptor = binding.Gesture;
                        _manager.RegisterGesture(gestureDescriptor);

                        if (binding.Actions != null)
                        {
                            foreach (var actionDescriptor in binding.Actions)
                            {
                                if (!ActionDescriptors.Contains(actionDescriptor))
                                {
                                    Logger.Warn($"Action \"{actionDescriptor.Name}\" not found!");
                                    continue;
                                }

                                _manager.BindAction(gestureDescriptor, actionDescriptor);
                            }
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
}
