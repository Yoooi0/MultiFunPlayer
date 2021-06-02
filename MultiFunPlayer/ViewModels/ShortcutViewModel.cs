using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Input;
using MultiFunPlayer.Common.Input.Gesture;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stylet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;

namespace MultiFunPlayer.ViewModels
{
    public class ShortcutViewModel : Screen, IHandle<AppSettingsMessage>, IDisposable
    {
        private readonly IShortcutManager _shortcutManager;
        private readonly BindableCollection<ShortcutModel> _shortcuts;
        private readonly Channel<IInputGesture> _gestureChannel;

        public string ActionsFilter { get; set; }
        public IReadOnlyCollection<ShortcutModel> Shortcuts { get; private set; }
        public bool IsSelectingGesture { get; private set; }

        public bool IsKeyboardKeysGestureEnabled { get; set; } = true;
        public bool IsMouseAxisGestureEnabled { get; set; } = false;
        public bool IsMouseButtonGestureEnabled { get; set; } = false;
        public bool IsHidAxisGestureEnabled { get; set; } = true;
        public bool IsHidButtonGestureEnabled { get; set; } = true;

        public ShortcutViewModel(IEventAggregator eventAggregator, IShortcutManager shortcutManager)
        {
            eventAggregator.Subscribe(this);

            _shortcutManager = shortcutManager;
            _shortcutManager.OnGesture += OnGesture;

            _shortcuts = new BindableCollection<ShortcutModel>();
            foreach (var actionDescriptor in _shortcutManager.Actions)
                _shortcuts.Add(new ShortcutModel(actionDescriptor));

            _gestureChannel = Channel.CreateBounded<IInputGesture>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });

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

        private async void OnGesture(object sender, IInputGesture gesture)
        {
            if (!IsSelectingGesture)
                return;

            switch (gesture)
            {
                case KeyboardGesture when !IsKeyboardKeysGestureEnabled:
                case MouseAxisGesture when !IsMouseAxisGestureEnabled:
                case MouseButtonGesture when !IsMouseButtonGestureEnabled:
                case HidAxisGesture when !IsHidAxisGestureEnabled:
                case HidButtonGesture when !IsHidButtonGestureEnabled:
                case IAxisInputGesture axisGesture when MathF.Abs(axisGesture.Delta) < 0.01f:
                    return;
            }

            await _gestureChannel.Writer.WriteAsync(gesture);
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
            && !IsMouseButtonGestureEnabled && !IsHidAxisGestureEnabled
            && !IsHidButtonGestureEnabled)
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
                _shortcutManager.RemoveShortcut(model.GestureDescriptor);

            model.GestureDescriptor = gesture?.Descriptor;
            if (gesture != null)
                _shortcutManager.RegisterShortcut(gesture.Descriptor, model.ActionDescriptor);
        }

        public void ClearGesture(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not ShortcutModel model)
                return;

            _shortcutManager.RemoveShortcut(model.GestureDescriptor);
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
                    { nameof(IsHidAxisGestureEnabled), JValue.FromObject(IsHidAxisGestureEnabled) },
                    { nameof(IsHidButtonGestureEnabled), JValue.FromObject(IsHidButtonGestureEnabled) },
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
                if (settings.TryGetValue<bool>(nameof(IsHidAxisGestureEnabled), out var isHidAxisGestureEnabled))
                    IsHidAxisGestureEnabled = isHidAxisGestureEnabled;
                if (settings.TryGetValue<bool>(nameof(IsHidButtonGestureEnabled), out var isHidButtonGestureEnabled))
                    IsHidButtonGestureEnabled = isHidButtonGestureEnabled;

                if (settings.TryGetValue<List<ShortcutModel>>("Bindings", out var loadedShortcuts))
                {
                    foreach (var shortcut in _shortcuts)
                    {
                        _shortcutManager.RemoveShortcut(shortcut.GestureDescriptor);
                        shortcut.GestureDescriptor = null;
                    }

                    foreach (var loadedShortcut in loadedShortcuts)
                    {
                        _shortcutManager.RegisterShortcut(loadedShortcut.GestureDescriptor, loadedShortcut.ActionDescriptor);

                        var shortcut = _shortcuts.FirstOrDefault(s => s.ActionDescriptor == loadedShortcut.ActionDescriptor);
                        if(shortcut != null)
                            shortcut.GestureDescriptor = loadedShortcut.GestureDescriptor;
                    }

                    UpdateShortcutsList();
                }
            }
        }

        protected virtual void Dispose(bool disposing) { }

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
