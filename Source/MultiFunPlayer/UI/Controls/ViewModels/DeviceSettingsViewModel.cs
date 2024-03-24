using MultiFunPlayer.Common;
using MultiFunPlayer.UI.Dialogs.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.Windows;

namespace MultiFunPlayer.UI.Controls.ViewModels;

internal sealed class DeviceSettingsViewModel : Screen, IHandle<SettingsMessage>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public DeviceSettings SelectedDevice { get; set; } = null;
    public ObservableConcurrentCollection<DeviceSettings> Devices { get; set; } = new(DeviceSettings.DefaultDevices);

    public DeviceSettingsViewModel(IEventAggregator eventAggregator)
    {
        DisplayName = "Device";
        eventAggregator.Subscribe(this);
    }

    public bool CanRemoveSelectedDevice => SelectedDevice?.IsDefault == false;
    public void OnRemoveSelectedDevice()
    {
        if (!CanRemoveSelectedDevice)
            return;

        var index = Devices.IndexOf(SelectedDevice);
        Devices.Remove(SelectedDevice);
        SelectedDevice = Devices[Math.Clamp(index, 0, Devices.Count - 1)];
    }

    public bool CanRenameSelectedDevice => SelectedDevice?.IsDefault == false;
    public async void OnRenameSelectedDevice()
    {
        if (!CanRenameSelectedDevice)
            return;

        var result = await DialogHelper.ShowAsync(new TextInputMessageDialog("Device name:", SelectedDevice.Name), "SettingsDialog") as string;
        if (string.IsNullOrWhiteSpace(result))
            return;

        if (Devices.Any(d => string.Equals(d.Name, result, StringComparison.OrdinalIgnoreCase)))
            return;

        SelectedDevice.Name = result;
    }

    public async void OnCloneDevice()
    {
        var result = await DialogHelper.ShowAsync(new TextInputMessageDialog("Device name:", $"{SelectedDevice.Name} (custom)"), "SettingsDialog").ConfigureAwait(true) as string;
        if (string.IsNullOrWhiteSpace(result))
            return;

        if (DeviceSettings.DefaultDevices.Any(d => string.Equals(d.Name, result, StringComparison.OrdinalIgnoreCase)))
            return;

        var device = SelectedDevice != null ? SelectedDevice.Clone(result) : new DeviceSettings() { Name = result };
        Devices.Add(device);
        SelectedDevice = device;
    }

    public bool CanExportSelectedDevice => SelectedDevice?.IsDefault == false;
    public void OnExportSelectedDevice()
    {
        if (SelectedDevice == null)
            return;

        try
        {
            var o = JObject.FromObject(SelectedDevice);
            o.Remove(nameof(DeviceSettings.IsDefault));

            var json = o.ToString(Formatting.Indented);
            Clipboard.SetText(json);
        }
        catch (Exception e)
        {
            Logger.Warn(e, "Device export failed");
            _ = DialogHelper.ShowErrorAsync(e, "Device export failed", "SettingsDialog");
        }
    }

    public void OnImportDevice()
    {
        if (!Clipboard.ContainsText())
            return;

        try
        {
            var o = JObject.Parse(Clipboard.GetText());
            o.Remove(nameof(DeviceSettings.IsDefault));

            var device = o.ToObject<DeviceSettings>();
            Devices.Add(device);
            SelectedDevice = device;
        }
        catch(Exception e)
        {
            Logger.Warn(e, "Device import failed");
            _ = DialogHelper.ShowErrorAsync(e, "Device import failed", "SettingsDialog");
        }
    }

    public void OnDeleteAxis(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not DeviceAxisSettings axisSettings)
            return;

        SelectedDevice.Axes.Remove(axisSettings);
    }

    public void OnAddAxis()
    {
        var letters = Enumerable.Range('A', 'Z' - 'A' + 1);
        var numbers = Enumerable.Range('0', '9' - '0' + 1);
        var availableName = letters.SelectMany(l => numbers.Select(n => $"{(char)l}{(char)n}"))
                                   .FirstOrDefault(x => !SelectedDevice.Axes.Any(a => string.Equals(a.Name, x, StringComparison.OrdinalIgnoreCase)));
        if (availableName == null)
            return;

        SelectedDevice.Axes.Add(new DeviceAxisSettings()
        {
            Name = availableName,
            FunscriptNames =
            [
                availableName
            ]
        });
    }

    public void Handle(SettingsMessage message)
    {
        if (message.Action == SettingsAction.Saving)
        {
            message.Settings[nameof(Devices)] = JArray.FromObject(Devices);
            message.Settings[nameof(SelectedDevice)] = SelectedDevice.Name;
        }
        else if (message.Action == SettingsAction.Loading)
        {
            if (message.Settings.TryGetValue<List<DeviceSettings>>(nameof(Devices), out var devices))
                Devices = new ObservableConcurrentCollection<DeviceSettings>(devices);
            if (message.Settings.TryGetValue<string>(nameof(SelectedDevice), out var selectedDevice))
                SelectedDevice = Devices.FirstOrDefault(d => string.Equals(d.Name, selectedDevice, StringComparison.OrdinalIgnoreCase)) ?? Devices[^1];
        }
    }
}
