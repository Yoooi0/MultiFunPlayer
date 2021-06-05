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
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;

namespace MultiFunPlayer.ViewModels
{
    public class ShortcutViewModel : Screen, IShortcutManager, IHandle<AppSettingsMessage>, IDisposable
    {
        protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<ShortcutActionDescriptor, IShortcutAction> _actions;
        private readonly ObservableConcurrentDictionary<IInputGestureDescriptor, ShortcutActionDescriptor> _bindings;
        private readonly BindableCollection<ShortcutModel> _shortcuts;
        private readonly Channel<IInputGesture> _gestureChannel;
        private readonly List<IInputProcessor> _processors;

        public string ActionsFilter { get; set; }
        public IReadOnlyCollection<ShortcutModel> Shortcuts { get; private set; }
        public bool IsSelectingGesture { get; private set; }

        public bool IsKeyboardKeysGestureEnabled { get; set; } = true;
        public bool IsMouseAxisGestureEnabled { get; set; } = false;
        public bool IsMouseButtonGestureEnabled { get; set; } = false;
        public bool IsGamepadAxisGestureEnabled { get; set; } = true;
        public bool IsGamepadButtonGestureEnabled { get; set; } = true;

        public ShortcutViewModel(IEnumerable<IInputProcessor> processors, IEventAggregator eventAggregator)
        {
            eventAggregator.Subscribe(this);

            _actions = new Dictionary<ShortcutActionDescriptor, IShortcutAction>();
            _bindings = new ObservableConcurrentDictionary<IInputGestureDescriptor, ShortcutActionDescriptor>();
            _shortcuts = new BindableCollection<ShortcutModel>();

            _gestureChannel = Channel.CreateBounded<IInputGesture>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });

            _processors = processors.ToList();
            foreach (var processor in _processors)
                processor.OnGesture += HandleGesture;

            UpdateShortcutsList();
            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "ActionsFilter")
                    UpdateShortcutsList();
            };
        }

        private void UpdateShortcutsList()
        {
            if (!string.IsNullOrWhiteSpace(ActionsFilter))
            {
                var filterWords = ActionsFilter.Split(' ');
                Shortcuts = _shortcuts?.Where(m =>
                   filterWords.All(w => (m.ActionDescriptor?.Name.Contains(w, StringComparison.InvariantCultureIgnoreCase) ?? false)
                                     || (m.GestureDescriptor?.ToString().Contains(w, StringComparison.InvariantCultureIgnoreCase) ?? false))
                ).ToList();
            }
            else
            {
                Shortcuts = _shortcuts;
            }
        }

        public void RegisterAction(IShortcutAction action)
        {
            if (_actions.ContainsKey(action.Descriptor))
                throw new NotSupportedException($"Duplicate action found \"{action.Descriptor}\"");

            Logger.Trace($"Registered \"{action}\" action");
            _actions[action.Descriptor] = action;
            _shortcuts.Add(new ShortcutModel(action.Descriptor));
        }

        private void RegisterShortcut(IInputGestureDescriptor gestureDescriptor, ShortcutActionDescriptor actionDescriptor)
        {
            if (gestureDescriptor == null)
                return;

            Logger.Debug($"Registered \"{gestureDescriptor}\" to \"{actionDescriptor}\"");
            _bindings[gestureDescriptor] = actionDescriptor;
        }

        private void RemoveShortcut(IInputGestureDescriptor gestureDescriptor)
        {
            if (gestureDescriptor == null)
                return;

            Logger.Debug($"Removed \"{gestureDescriptor}\" action");
            _bindings.Remove(gestureDescriptor, out var _);
        }

        private async void HandleGesture(object sender, IInputGesture gesture)
        {
            if (IsSelectingGesture)
            {
                switch (gesture)
                {
                    case KeyboardGesture when !IsKeyboardKeysGestureEnabled:
                    case MouseAxisGesture when !IsMouseAxisGestureEnabled:
                    case MouseButtonGesture when !IsMouseButtonGestureEnabled:
                    case GamepadAxisGesture when !IsGamepadAxisGestureEnabled:
                    case GamepadButtonGesture when !IsGamepadButtonGestureEnabled:
                    case IAxisInputGesture axisGesture when MathF.Abs(axisGesture.Delta) < 0.01f:
                        return;
                }

                await _gestureChannel.Writer.WriteAsync(gesture);
            }

            if (_bindings.TryGetValue(gesture.Descriptor, out var actionDescriptor)
             && _actions.TryGetValue(actionDescriptor, out var action))
                action.Invoke(gesture);
        }

        private bool ValidateGesture(IInputGesture gesture, ShortcutModel model)
        {
            if (_shortcuts.Any(m => m != model && gesture.Descriptor.Equals(m.GestureDescriptor)))
                return false;

            switch (gesture)
            {
                case not IAxisInputGesture when model.ActionDescriptor.Type == ShortcutActionType.Axis:
                case IAxisInputGesture when model.ActionDescriptor.Type != ShortcutActionType.Axis:
                    return false;
            }

            return true;
        }

        public async void SelectGesture(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not ShortcutModel model)
                return;

            if (!IsKeyboardKeysGestureEnabled && !IsMouseAxisGestureEnabled
            && !IsMouseButtonGestureEnabled && !IsGamepadAxisGestureEnabled
            && !IsGamepadButtonGestureEnabled)
                return;

            await TrySelectGestureAsync(model).ConfigureAwait(true);
        }

        private async Task TrySelectGestureAsync(ShortcutModel model)
        {
            var tryCount = 0;
            var gesture = default(IInputGesture);

            while (_gestureChannel.Reader.TryRead(out var _)) ;

            IsSelectingGesture = true;
            do
            {
                await _gestureChannel.Reader.WaitToReadAsync().ConfigureAwait(true);
                gesture = await _gestureChannel.Reader.ReadAsync().ConfigureAwait(true);
            } while (!ValidateGesture(gesture, model) && tryCount++ < 5);

            IsSelectingGesture = false;
            if (tryCount >= 5)
                gesture = null;

            if (model.GestureDescriptor != null)
                RemoveShortcut(model.GestureDescriptor);

            model.GestureDescriptor = gesture?.Descriptor;
            if (gesture != null)
                RegisterShortcut(gesture.Descriptor, model.ActionDescriptor);
        }

        public void ClearGesture(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not ShortcutModel model)
                return;

            RemoveShortcut(model.GestureDescriptor);
            model.GestureDescriptor = null;
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
                    { "Bindings", JArray.FromObject(_shortcuts.Where(s => s.GestureDescriptor != null)) },
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

                if (settings.TryGetValue<List<ShortcutModel>>("Bindings", out var loadedShortcuts))
                {
                    foreach (var shortcut in _shortcuts)
                    {
                        RemoveShortcut(shortcut.GestureDescriptor);
                        shortcut.GestureDescriptor = null;
                    }

                    foreach (var loadedShortcut in loadedShortcuts)
                    {
                        RegisterShortcut(loadedShortcut.GestureDescriptor, loadedShortcut.ActionDescriptor);

                        var shortcut = _shortcuts.FirstOrDefault(s => s.ActionDescriptor == loadedShortcut.ActionDescriptor);
                        if (shortcut != null)
                            shortcut.GestureDescriptor = loadedShortcut.GestureDescriptor;
                        else
                            Logger.Warn($"Action \"{loadedShortcut.ActionDescriptor}\" not found!");
                    }

                    UpdateShortcutsList();
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            foreach (var processor in _processors)
                processor.OnGesture -= HandleGesture;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class ShortcutModel : PropertyChangedBase
    {
        public ShortcutModel(ShortcutActionDescriptor actionDescriptor) => ActionDescriptor = actionDescriptor;

        [JsonProperty] public ShortcutActionDescriptor ActionDescriptor { get; }
        [JsonProperty(TypeNameHandling = TypeNameHandling.Auto)] public IInputGestureDescriptor GestureDescriptor { get; set; }
    }
}
