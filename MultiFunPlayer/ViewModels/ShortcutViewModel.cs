using MultiFunPlayer.Common.Input;
using MultiFunPlayer.Common.Input.Gesture;
using Stylet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace MultiFunPlayer.ViewModels
{
    public class ShortcutViewModel : Screen, IDisposable
    {
        private readonly IShortcutManager _shortcutManager;
        private readonly BindableCollection<ShortcutModel> _shortcuts;
        private TaskCompletionSource<IInputGesture> _gestureSource;

        public string ActionsFilter { get; set; }
        public IReadOnlyCollection<ShortcutModel> Shortcuts { get; private set; }

        public bool IsKeyboardKeysGestureEnabled { get; set; } = true;
        public bool IsMouseAxisGestureEnabled { get; set; } = false;
        public bool IsMouseButtonGestureEnabled { get; set; } = false;
        public bool IsHidAxisGestureEnabled { get; set; } = true;
        public bool IsHidButtonGestureEnabled { get; set; } = true;

        public bool IsSelectingGesture => _gestureSource != null;

        public ShortcutViewModel(IShortcutManager shortcutManager)
        {
            _shortcutManager = shortcutManager;
            _shortcutManager.OnGesture += OnGesture;

            _shortcuts = new BindableCollection<ShortcutModel>();
            foreach (var action in _shortcutManager.Actions)
                _shortcuts.Add(new ShortcutModel() { ActionName = action });
            foreach (var action in _shortcutManager.AxisActions)
                _shortcuts.Add(new ShortcutModel() { ActionName = action, IsAxisAction = true });

            UpdateAvailableActions();
            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "ActionsFilter")
                    UpdateAvailableActions();
            };
        }

        private void UpdateAvailableActions()
        {
            if (!string.IsNullOrWhiteSpace(ActionsFilter))
            {
                var filterWords = ActionsFilter.Split(' ');
                Shortcuts = _shortcuts?.Where(m =>
                   filterWords.All(w => (m.ActionName?.Contains(w, StringComparison.InvariantCultureIgnoreCase) ?? false)
                                     || (m.GestureDescriptor?.ToString().Contains(w, StringComparison.InvariantCultureIgnoreCase) ?? false))
                ).ToList();
            }
            else
            {
                Shortcuts = _shortcuts;
            }
        }

        private void OnGesture(object sender, IInputGesture gesture)
        {
            if (_gestureSource == null)
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

            _gestureSource?.SetResult(gesture);
        }

        private bool ValidateGesture(IInputGesture gesture, ShortcutModel model)
        {
            if (_shortcuts.Any(m => m != model && gesture.Equals(m.GestureDescriptor)))
                return false;

            switch (gesture)
            {
                case not IAxisInputGesture when model.IsAxisAction:
                case IAxisInputGesture when !model.IsAxisAction:
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
            do
            {
                _gestureSource = new TaskCompletionSource<IInputGesture>();
                NotifyOfPropertyChange(nameof(IsSelectingGesture));

                gesture = await _gestureSource.Task.ConfigureAwait(true);
            } while (!ValidateGesture(gesture, model) && tryCount++ < 5);

            if (tryCount >= 5)
                gesture = null;

            _gestureSource = null;
            NotifyOfPropertyChange(nameof(IsSelectingGesture));

            if(model.GestureDescriptor != null)
                _shortcutManager.RemoveShortcut(model.GestureDescriptor);

            model.GestureDescriptor = gesture.Descriptor;
            if (gesture != null)
                _shortcutManager.RegisterShortcut(gesture.Descriptor, model.ActionName);
        }

        public void ClearGesture(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not ShortcutModel model)
                return;

            _shortcutManager.RemoveShortcut(model.GestureDescriptor);
            model.GestureDescriptor = null;
        }

        protected virtual void Dispose(bool disposing) { }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    public class ShortcutModel : PropertyChangedBase
    {
        public string ActionName { get; init; }
        public bool IsAxisAction { get; init; }
        public IInputGestureDescriptor GestureDescriptor { get; set; }
    }
}
