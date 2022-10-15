using MultiFunPlayer.Common;
using MultiFunPlayer.UI.Dialogs.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.Windows;

namespace MultiFunPlayer.UI.Controls.ViewModels;

public class DeviceSettingsViewModel : Screen, IHandle<SettingsMessage>
{
    public DeviceSettingsModel SelectedDevice { get; set; } = null;
    public ObservableConcurrentCollection<DeviceSettingsModel> Devices { get; set; } = new(DefaultDevices);

    public DeviceSettingsViewModel(IEventAggregator eventAggregator)
    {
        DisplayName = "Device";
        eventAggregator.Subscribe(this);
    }

    public bool CanRemoveSelectedDevice => Devices?.Count > 1;
    public void OnRemoveSelectedDevice()
    {
        if (!CanRemoveSelectedDevice)
            return;

        var index = Devices.IndexOf(SelectedDevice);
        Devices.Remove(SelectedDevice);
        SelectedDevice = Devices[MathUtils.Clamp(index, 0, Devices.Count - 1)];
    }

    public async void OnRenameSelectedDevice()
    {
        var result = await DialogHelper.ShowAsync(new TextInputMessageDialogViewModel("Device name:", SelectedDevice.Name), "SettingsDialog") as string;
        if (string.IsNullOrWhiteSpace(result))
            return;

        if (Devices.Any(d => string.Equals(d.Name, result, StringComparison.InvariantCultureIgnoreCase)))
            return;

        SelectedDevice.Name = result;
    }

    public async void OnCloneDevice()
    {
        var result = await DialogHelper.ShowAsync(new TextInputMessageDialogViewModel("Device name:", $"{SelectedDevice.Name} (custom)"), "SettingsDialog")
                                       .ConfigureAwait(true) as string;
        if (string.IsNullOrWhiteSpace(result))
            return;

        if (DefaultDevices.Any(d => string.Equals(d.Name, result, StringComparison.InvariantCultureIgnoreCase)))
            return;

        var device = SelectedDevice != null ? SelectedDevice.Clone(result) : new DeviceSettingsModel() { Name = result };
        Devices.Add(device);
        SelectedDevice = device;
    }

    public void OnDeleteAxis(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not DeviceAxisSettingsModel axisSettings)
            return;

        SelectedDevice.Axes.Remove(axisSettings);
    }

    public void OnAddAxis()
    {
        var letters = Enumerable.Range('A', 'Z' - 'A' + 1);
        var numbers = Enumerable.Range('0', '9' - '0' + 1);
        var availableName = letters.SelectMany(l => numbers.Select(n => $"{(char)l}{(char)n}"))
                                   .FirstOrDefault(x => !SelectedDevice.Axes.Any(a => string.Equals(a.Name, x, StringComparison.InvariantCultureIgnoreCase)));
        if (availableName == null)
            return;

        SelectedDevice.Axes.Add(new DeviceAxisSettingsModel()
        {
            Name = availableName,
            FunscriptNames = new()
            {
                availableName
            }
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
            if (message.Settings.TryGetValue<List<DeviceSettingsModel>>(nameof(Devices), out var devices))
                Devices = new ObservableConcurrentCollection<DeviceSettingsModel>(devices);
            if (message.Settings.TryGetValue<string>(nameof(SelectedDevice), out var selectedDevice))
                SelectedDevice = Devices.FirstOrDefault(d => string.Equals(d.Name, selectedDevice, StringComparison.InvariantCultureIgnoreCase)) ?? Devices[^1];
        }
    }

    public static readonly IReadOnlyList<DeviceSettingsModel> DefaultDevices = new List<DeviceSettingsModel>()
    {
        new()
        {
            Name = "TCode-0.2",
            OutputPrecision = 3,
            Default = true,
            Axes = new()
            {
                new() { Name = "L0", FriendlyName = "Up/Down", FunscriptNames = new() { "stroke", "L0", "up" }, LoadUnnamedScript = true, Enabled = true, DefaultValue = 0.5, },
                new() { Name = "L1", FriendlyName = "Forward/Backward", FunscriptNames = new() { "surge", "L1", "forward" }, Enabled = true, DefaultValue = 0.5, },
                new() { Name = "L2", FriendlyName = "Left/Right", FunscriptNames = new() { "sway", "L2", "left" }, Enabled = true, DefaultValue = 0.5, },
                new() { Name = "R0", FriendlyName = "Twist", FunscriptNames = new() { "twist", "R0", "yaw" }, Enabled = true, DefaultValue = 0.5, },
                new() { Name = "R1", FriendlyName = "Roll", FunscriptNames = new() { "roll", "R1" }, Enabled = true, DefaultValue = 0.5, },
                new() { Name = "R2", FriendlyName = "Pitch", FunscriptNames = new() { "pitch", "R2" }, Enabled = true, DefaultValue = 0.5, },
                new() { Name = "V0", FriendlyName = "Vibrate", FunscriptNames = new() { "vib", "V0" }, Enabled = false, DefaultValue = 0, },
                new() { Name = "V1", FriendlyName = "Pump", FunscriptNames = new() { "pump", "lube", "V1" }, Enabled = false, DefaultValue = 0, },
                new() { Name = "L3", FriendlyName = "Suction", FunscriptNames = new() { "suck", "valve", "L3" }, Enabled = false, DefaultValue = 0, }
            }
        },
        new()
        {
            Name = "TCode-0.3",
            OutputPrecision = 4,
            Default = true,
            Axes = new()
            {
                new() { Name = "L0", FriendlyName = "Up/Down", FunscriptNames = new() { "stroke", "L0", "up" }, LoadUnnamedScript = true, Enabled = true, DefaultValue = 0.5, },
                new() { Name = "L1", FriendlyName = "Forward/Backward", FunscriptNames = new() { "surge", "L1", "forward"}, Enabled = true, DefaultValue = 0.5, },
                new() { Name = "L2", FriendlyName = "Left/Right", FunscriptNames = new() { "sway", "L2", "left" }, Enabled = true, DefaultValue = 0.5, },
                new() { Name = "R0", FriendlyName = "Twist", FunscriptNames = new() { "twist", "R0", "yaw" }, Enabled = true, DefaultValue = 0.5, },
                new() { Name = "R1", FriendlyName = "Roll", FunscriptNames = new() { "roll", "R1" }, Enabled = true, DefaultValue = 0.5, },
                new() { Name = "R2", FriendlyName = "Pitch", FunscriptNames = new() { "pitch", "R2" }, Enabled = true, DefaultValue = 0.5, },
                new() { Name = "V0", FriendlyName = "Vibrate", FunscriptNames = new() { "vib", "V0" }, Enabled = false, DefaultValue = 0, },
                new() { Name = "V1", FriendlyName = "Pump", FunscriptNames = new() { "pump", "V1" }, Enabled = false, DefaultValue = 0, },
                new() { Name = "A0", FriendlyName = "Valve", FunscriptNames = new() { "valve", "A0" }, Enabled = false, DefaultValue = 0, },
                new() { Name = "A1", FriendlyName = "Suction", FunscriptNames = new() { "suck", "A1" }, Enabled = false, DefaultValue = 0, },
                new() { Name = "A2", FriendlyName = "Lube", FunscriptNames = new() { "lube", "A2" }, Enabled = false, DefaultValue = 0, }
            }
        }
    };
}

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class DeviceSettingsModel : PropertyChangedBase
{
    [JsonProperty] public string Name { get; set; } = null;
    [JsonProperty] public bool Default { get; set; } = false;
    [JsonProperty] public int OutputPrecision { get; set; } = 3;
    [JsonProperty] public ObservableConcurrentCollection<DeviceAxisSettingsModel> Axes { get; set; } = new();

    public DeviceSettingsModel Clone(string name) => new()
    {
        Name = name,
        Default = false,
        OutputPrecision = OutputPrecision,
        Axes = new(Axes.Select(a => a.Clone()))
    };
}

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class DeviceAxisSettingsModel : PropertyChangedBase
{
    [JsonProperty] public string Name { get; set; } = null;
    [JsonProperty] public string FriendlyName { get; set; } = null;
    [JsonProperty] public ObservableConcurrentCollection<string> FunscriptNames { get; set; } = new();
    [JsonProperty] public bool LoadUnnamedScript { get; set; } = false;
    [JsonProperty] public double DefaultValue { get; set; } = 0;
    [JsonProperty] public bool Enabled { get; set; } = false;

    public DeviceAxisSettingsModel Clone() => new()
    {
        Name = Name,
        FriendlyName = FriendlyName,
        FunscriptNames = new(FunscriptNames),
        LoadUnnamedScript = LoadUnnamedScript,
        DefaultValue = DefaultValue,
        Enabled = Enabled
    };
}