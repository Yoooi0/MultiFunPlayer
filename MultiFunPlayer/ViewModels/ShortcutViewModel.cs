using MultiFunPlayer.Common.Input;
using MultiFunPlayer.Common.Input.Gesture;
using Stylet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MultiFunPlayer.ViewModels
{
    public class ShortcutViewModel : Screen, IDisposable
    {
        private readonly IShortcutManager _shortcutManager;
        private TaskCompletionSource<IInputGesture> _gestureSource;

        public string ActionsFilter { get; set; }
        public string SelectedAction { get; set; }
        public IInputGesture SelectedGesture { get; private set; }
        public IReadOnlyCollection<string> AvailableActions { get; private set; }

        public bool IsKeyboardKeysGestureEnabled { get; set; } = true;
        public bool IsMouseAxisGestureEnabled { get; set; } = false;
        public bool IsMouseButtonGestureEnabled { get; set; } = false;
        public bool IsHidAxisGestureEnabled { get; set; } = true;
        public bool IsHidButtonGestureEnabled { get; set; } = true;

        public IReadOnlyDictionary<IInputGesture, string> Shortcuts => _shortcutManager.Shortcuts;

        public bool IsSelectingGesture => _gestureSource != null;
        public bool CanAddShortcut => SelectedGesture != null && SelectedAction != null;

        public ShortcutViewModel(IShortcutManager shortcutManager)
        {
            _shortcutManager = shortcutManager;
            _shortcutManager.OnGesture += OnGesture;

            NotifyOfPropertyChange(nameof(Shortcuts));
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
                default:
                    break;
            }

            if (Shortcuts.ContainsKey(gesture))
                return;

            _gestureSource.SetResult(gesture);
            _gestureSource = null;

            NotifyOfPropertyChange(nameof(IsSelectingGesture));
        }

        public void OnComboBoxKeyUp(object sender, KeyEventArgs e)
        {
            if (sender is not ComboBox combobox)
                return;

            _ = Execute.OnUIThreadAsync(async () =>
            {
                // rate limit hack
                var text = combobox.Text;
                await Task.Delay(500).ConfigureAwait(true);
                if (!string.Equals(combobox.Text, text, StringComparison.InvariantCulture))
                    return;

                UpdateAvailableActions();
            });
        }

        public void OnAddShortcut()
        {
            if (!CanAddShortcut)
                return;

            _shortcutManager.RegisterShortcut(SelectedGesture, SelectedAction);
            NotifyOfPropertyChange(nameof(Shortcuts));

            SelectedGesture = null;
            SelectedAction = null;
        }

        public void OnRemoveShortcut(object sender, EventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not KeyValuePair<IInputGesture, string> pair)
                return;

            _shortcutManager.RemoveShortcut(pair.Key);
            NotifyOfPropertyChange(nameof(Shortcuts));
        }

        private void UpdateAvailableActions()
        {
            var availableActions = SelectedGesture switch
            {
                null => null,
                IAxisInputGesture => _shortcutManager.AxisActions,
                _ => _shortcutManager.Actions
            } as IEnumerable<string>;

            if (!string.IsNullOrWhiteSpace(ActionsFilter))
            {
                availableActions = availableActions?.Where(a =>
                    ActionsFilter.Split(' ').All(w => a.Contains(w, StringComparison.InvariantCultureIgnoreCase))
                );
            }

            var usedActions = Shortcuts.Values;
            availableActions = availableActions.Where(a => !usedActions.Contains(a));

            AvailableActions = availableActions.ToList();
        }

        public async void WaitForGesture()
        {
            if (!IsKeyboardKeysGestureEnabled && !IsMouseAxisGestureEnabled
            && !IsMouseButtonGestureEnabled && !IsHidAxisGestureEnabled
            && !IsHidButtonGestureEnabled)
                return;

            _gestureSource = new TaskCompletionSource<IInputGesture>();
            NotifyOfPropertyChange(nameof(IsSelectingGesture));

            SelectedGesture = await _gestureSource.Task;
            UpdateAvailableActions();
        }

        protected virtual void Dispose(bool disposing) { }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
