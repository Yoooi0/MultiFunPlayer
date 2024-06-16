using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.Shortcut;
using MultiFunPlayer.UI.Dialogs.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Reflection;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Controls.ViewModels;

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
internal sealed class ShortcutSettingsViewModel : Screen, IHandle<SettingsMessage>, IHandle<IInputGesture>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly IShortcutManager _shortcutManager;
    private readonly IShortcutFactory _shortcutFactory;
    private readonly Channel<IInputGesture> _gestureChannel;

    public string ActionsFilter { get; set; }
    public ICollectionView AvailableActionsView { get; }
    public IReadOnlyObservableConcurrentCollection<string> AvailableActions => _shortcutManager.AvailableActions;

    [JsonProperty(ItemTypeNameHandling = TypeNameHandling.Objects)]
    public IReadOnlyObservableConcurrentCollection<IShortcut> Shortcuts => _shortcutManager.Shortcuts;
    public IShortcut SelectedShortcut { get; set; }

    public bool IsCapturingGestures { get; private set; }
    public ObservableConcurrentCollection<IInputGestureDescriptor> CapturedGestures { get; }
    public IInputGestureDescriptor SelectedCapturedGesture { get; set; }

    public IReadOnlyCollection<Type> ShortcutTypes { get; }
    public Type SelectedShortcutType { get; set; }

    public ShortcutSettingsViewModel(IShortcutManager shortcutManager, IShortcutFactory shortcutFactory, IEventAggregator eventAggregator)
    {
        DisplayName = "Shortcut";
        _shortcutManager = shortcutManager;
        _shortcutFactory = shortcutFactory;

        Logger.Debug($"Initialized with {shortcutManager.AvailableActions.Count} available actions");

        eventAggregator.Subscribe(this, [EventAggregator.DefaultChannel, IInputProcessor.EventAggregatorChannelName]);

        CapturedGestures = [];
        ShortcutTypes = [.. ReflectionUtils.FindImplementations<IShortcut>().OrderBy(x => x.GetCustomAttribute<DisplayNameAttribute>().DisplayName)];

        AvailableActionsView = CollectionViewSource.GetDefaultView(AvailableActions);
        AvailableActionsView.Filter = o =>
        {
            if (o is not string actionName)
                return false;
            if (SelectedShortcut == null)
                return false;

            if (!_shortcutManager.ActionAcceptsGestureData(actionName, SelectedShortcut.OutputDataType))
                return false;

            if (!string.IsNullOrWhiteSpace(ActionsFilter))
            {
                var filterWords = ActionsFilter.Split(' ');
                if (!filterWords.All(w => actionName.Contains(w, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }

            return true;
        };

        _gestureChannel = Channel.CreateUnbounded<IInputGesture>(new UnboundedChannelOptions()
        {
            SingleReader = true,
            SingleWriter = true
        });

        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(ActionsFilter) or nameof(SelectedShortcut))
                AvailableActionsView.Refresh();
        };

        RegisterActions(_shortcutManager);
    }

    protected override void OnActivate() => _shortcutManager.HandleGestures = false;
    protected override void OnDeactivate() => _shortcutManager.HandleGestures = true;

    public void Handle(IInputGesture gesture)
    {
        if (IsCapturingGestures)
            _gestureChannel.Writer.TryWrite(gesture);
    }

    public async void CaptureGestures(object sender, RoutedEventArgs e)
    {
        if (IsCapturingGestures)
            return;

        if (SelectedShortcutType == null)
            return;

        using var captureCancellationSource = new CancellationTokenSource(5000);
        var token = captureCancellationSource.Token;

        while (_gestureChannel.Reader.TryRead(out var _)) ;
        CapturedGestures.Clear();

        IsCapturingGestures = true;

        try
        {
            do
            {
                _ = await _gestureChannel.Reader.WaitToReadAsync(token);
                var gesture = await _gestureChannel.Reader.ReadAsync(token);
                if (IShortcut.AcceptsGesture(SelectedShortcutType, gesture) && !CapturedGestures.Contains(gesture.Descriptor))
                {
                    CapturedGestures.Add(gesture.Descriptor);
                    captureCancellationSource.CancelAfter(5000);
                }
            } while (!token.IsCancellationRequested);
        }
        catch (OperationCanceledException) { }

        IsCapturingGestures = false;
    }

    public void AddShortcut(object sender, RoutedEventArgs e)
    {
        if (SelectedShortcutType == null)
            return;
        if (SelectedCapturedGesture == null)
            return;

        var shortcut = _shortcutFactory.CreateShortcut(SelectedShortcutType, SelectedCapturedGesture);
        SelectedShortcut = _shortcutManager.AddShortcut(shortcut);
    }

    public void RemoveShortcut(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IShortcut shortcut)
            return;

        _shortcutManager.RemoveShortcut(shortcut);
    }

    public void AssignAction(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not string actionName)
            return;
        if (SelectedShortcut == null)
            return;

        _shortcutManager.BindAction(SelectedShortcut, actionName);
    }

    public void RemoveAssignedAction(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IShortcutActionConfiguration configuration)
            return;
        if (SelectedShortcut == null)
            return;

        _shortcutManager.UnbindAction(SelectedShortcut, configuration);
    }

    public void MoveAssignedActionUp(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IShortcutActionConfiguration configuration)
            return;
        if (SelectedShortcut == null)
            return;

        var configurations = SelectedShortcut.Configurations;
        var index = configurations.IndexOf(configuration);
        if (index == 0)
            return;

        configurations.Move(index, index - 1);
    }

    public void ConfigureAssignedAction(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IShortcutActionConfiguration configuration)
            return;

        _ = DialogHelper.ShowOnUIThreadAsync(new ShortcutActionConfigurationDialog(configuration), "SettingsDialog");
    }

    public void MoveAssignedActionDown(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IShortcutActionConfiguration configuration)
            return;
        if (SelectedShortcut == null)
            return;

        var configurations = SelectedShortcut.Configurations;
        var index = configurations.IndexOf(configuration);
        if (index == configurations.Count - 1)
            return;

        configurations.Move(index, index + 1);
    }

    public void OnSelectedShortcutChanged()
    {
        if (SelectedShortcut == null)
            ActionsFilter = null;
    }

    public void OnSelectedShortcutTypeChanged()
        => CapturedGestures.Clear();

    public void Handle(SettingsMessage message)
    {
        if (message.Action == SettingsAction.Saving)
        {
            if (!message.Settings.EnsureContainsObjects("Shortcut")
             || !message.Settings.TryGetObject(out var settings, "Shortcut"))
                return;

            settings.MergeAll(JObject.FromObject(this));
        }
        else if (message.Action == SettingsAction.Loading)
        {
            if (!message.Settings.TryGetObject(out var settings, "Shortcut"))
                return;

            if (settings.TryGetValue<List<IShortcut>>(nameof(Shortcuts), new JsonSerializerSettings() { TypeNameHandling = TypeNameHandling.Objects }, out var shortcuts))
            {
                _shortcutManager.ClearShortcuts();
                foreach (var shortcut in shortcuts)
                    _shortcutManager.AddShortcut(shortcut);
            }
        }
    }

    private void RegisterActions(IShortcutManager s)
    {
        #region Shortcut::Enabled
        void UpdateSettings(string shortcutName, Action<IShortcut> callback)
        {
            var shortcut = Shortcuts.FirstOrDefault(x => string.Equals(x.Name, shortcutName, StringComparison.Ordinal));
            if (shortcut == null)
                return;
            callback(shortcut);
        }

        s.RegisterAction<string, bool>("Shortcut::Enabled::Set",
            s => s.WithLabel("Target shortcut name"),
            s => s.WithLabel("Enabled"),
            (shortcutName, enabled) => UpdateSettings(shortcutName, s => s.Enabled = enabled));

        s.RegisterAction<string>("Shortcut::Enabled::Toggle",
            s => s.WithLabel("Target shortcut name"),
            shortcutName => UpdateSettings(shortcutName, s => s.Enabled = !s.Enabled));
        #endregion
    }
}
