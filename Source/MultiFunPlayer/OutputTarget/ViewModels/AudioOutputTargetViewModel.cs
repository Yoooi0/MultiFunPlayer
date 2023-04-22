using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.UI;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Newtonsoft.Json.Linq;
using NLog;
using PropertyChanged;
using Stylet;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("Audio")]
internal class AudioOutputTargetViewModel : ThreadAbstractOutputTarget
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public ObservableConcurrentCollection<IAudioDeviceModel> AvailableAudioDevices { get; }
    public IAudioDeviceModel SelectedAudioDevice { get; set; }

    public int SampleRate { get; set; } = 48000;
    public SignalGeneratorType SignalType { get; set; } = SignalGeneratorType.Sin;
    public DeviceAxisMappedValue Frequency { get; } = new DeviceAxisMappedValue() { ValueFrom = 100, ValueTo = 440, Value = 200 };
    public DeviceAxisMappedValue Amplitude { get; } = new DeviceAxisMappedValue() { ValueFrom = 0, ValueTo = 1, Value = 1 };
    public DeviceAxisMappedValue Balance { get; } = new DeviceAxisMappedValue() { ValueFrom = -1, ValueTo = 1, Value = 0 };

    public override ConnectionStatus Status { get; protected set; }

    public AudioOutputTargetViewModel(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        : base(instanceIndex, eventAggregator, valueProvider)
    {
        AvailableAudioDevices = new ObservableConcurrentCollection<IAudioDeviceModel>();

        var size = Marshal.SizeOf<WaveOutCapabilities>();
        for (int i = 0; i < WaveInterop.waveOutGetNumDevs(); i++)
        {
            var result = WaveInterop.waveOutGetDevCaps(i, out var capabilities, size);
            if (result == NAudio.MmResult.NoError)
                AvailableAudioDevices.Add(new WaveOutAudioDeviceModel(capabilities, i));
        }

        foreach (var device in DirectSoundOut.Devices)
            AvailableAudioDevices.Add(new DirectSoundAudioDeviceModel(device));

        var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            AvailableAudioDevices.Add(new WasapiAudioDeviceModel(device));

        foreach (var asio in AsioOut.GetDriverNames())
            AvailableAudioDevices.Add(new AsioAudioDeviceModel(asio));
    }

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    protected override void Run(CancellationToken token)
    {
        try
        {
            using var wavePlayer = SelectedAudioDevice.CreateWavePlayer();
            var sampleProvider = new SignalGenerator(SampleRate)
            {
                Type = SignalGeneratorType.Sin
            };

            wavePlayer.Init(sampleProvider);
            wavePlayer.Play();

            Status = ConnectionStatus.Connected;
            EventAggregator.Publish(new SyncRequestMessage());

            FixedUpdate(() => !token.IsCancellationRequested, elapsed =>
            {
                Logger.Trace("Begin FixedUpdate [Elapsed: {0}]", elapsed);
                UpdateValues();

                sampleProvider.Type = SignalType;
                sampleProvider.Frequency = Frequency.Map(Values);
                sampleProvider.Amplitude = Amplitude.Map(Values);
                sampleProvider.Balance = Balance.Map(Values);
            });
        }
        catch (Exception e)
        {
            Logger.Error(e, $"{Identifier} failed with exception");
            _ = DialogHelper.ShowErrorAsync(e, $"{Identifier} failed with exception", "RootDialog");
        }
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);
    }

    public override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);
    }

    public override void UnregisterActions(IShortcutManager s)
    {
        base.UnregisterActions(s);
    }

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token) => await ValueTask.FromResult(true);
}

[AddINotifyPropertyChangedInterface]
internal partial class DeviceAxisMappedValue
{
    public DeviceAxis Axis { get; set; }
    public double Value { get; set; }
    public double ValueFrom { get; set; }
    public double ValueTo { get; set; }

    protected void OnValueFromChanged()
    {
        if (ValueFrom > ValueTo)
            (ValueTo, ValueFrom) = (ValueFrom, ValueTo);
    }

    protected void OnValueToChanged()
    {
        if (ValueTo < ValueFrom)
            (ValueTo, ValueFrom) = (ValueFrom, ValueTo);
    }

    public double Map(IDictionary<DeviceAxis, double> values)
    {
        Value = Axis != null ? MathUtils.Map(values[Axis], 0, 1, ValueFrom, ValueTo)
                             : Math.Clamp(Value, ValueFrom, ValueTo);

        return Value;
    }
}

internal enum AudioDeviceDriver
{
    Asio,
    WaveOut,
    DirectSound,
    Wasapi
}

internal interface IAudioDeviceModel
{
    public string Name { get; }
    public AudioDeviceDriver Driver { get; }
    public IWavePlayer CreateWavePlayer();
}

internal class AsioAudioDeviceModel : IAudioDeviceModel
{
    public string Name { get; }
    public AudioDeviceDriver Driver => AudioDeviceDriver.Asio;
    public string DisplayName => $"{Name} ({Driver})";

    public int DesiredLatency { get; set; } = 300;

    public AsioAudioDeviceModel(string name) => Name = name;
    public IWavePlayer CreateWavePlayer() => new AsioOut(Name);
}

internal class WaveOutAudioDeviceModel : IAudioDeviceModel
{
    private readonly int _deviceIndex;
    private readonly Guid _guid;

    public string Name { get; }
    public AudioDeviceDriver Driver => AudioDeviceDriver.WaveOut;
    public string DisplayName => $"{Name} ({Driver})";

    public int DesiredLatency { get; set; } = 300;

    public WaveOutAudioDeviceModel(WaveOutCapabilities capabilities, int deviceIndex)
    {
        _deviceIndex = deviceIndex;
        _guid = capabilities.NameGuid;

        Name = capabilities.ProductName;
    }

    public IWavePlayer CreateWavePlayer() => new WaveOutEvent()
    {
        DeviceNumber = _deviceIndex,
        DesiredLatency = DesiredLatency
    };
}

internal class DirectSoundAudioDeviceModel : IAudioDeviceModel
{
    private readonly Guid _guid;

    public string Name { get; }
    public AudioDeviceDriver Driver => AudioDeviceDriver.DirectSound;
    public string DisplayName => $"{Name} ({Driver})";

    public int DesiredLatency { get; set; } = 40;

    public DirectSoundAudioDeviceModel(DirectSoundDeviceInfo info)
    {
        _guid = info.Guid;
        Name = info.Description;
    }

    public IWavePlayer CreateWavePlayer() => new DirectSoundOut(_guid, DesiredLatency);
}

internal class WasapiAudioDeviceModel : IAudioDeviceModel
{
    private readonly string _id;

    public string Name { get; }
    public AudioDeviceDriver Driver => AudioDeviceDriver.Wasapi;
    public string DisplayName => $"{Name} ({Driver})";

    public AudioClientShareMode ShareMode { get; set; } = AudioClientShareMode.Shared;
    public bool UseEventSync { get; set; } = true;
    public int DesiredLatency { get; set; } = 200;

    public WasapiAudioDeviceModel(MMDevice device)
    {
        _id = device.ID;
        Name = device.DeviceFriendlyName;
    }

    public IWavePlayer CreateWavePlayer()
    {
        var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(_id);
        return new WasapiOut(device, ShareMode, UseEventSync, DesiredLatency);
    }
}

internal class SignalGenerator : ISampleProvider
{
    private double _phase;

    public SignalGenerator(int sampleRate)
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);

        Type = SignalGeneratorType.Sin;
        Frequency = 240.0;
        Amplitude = 1;
    }

    public WaveFormat WaveFormat { get; }

    public double Frequency { get; set; }
    public double Amplitude { get; set; }
    public double Balance { get; set; }
    public SignalGeneratorType Type { get; set; }

    public int Read(float[] buffer, int offset, int count)
    {
        var phaseStep = 2 * Frequency / WaveFormat.SampleRate;
        for (var sampleCount = 0; sampleCount < count; sampleCount += WaveFormat.Channels)
        {
            var sampleValue = GetSampleValue();
            _phase += phaseStep;

            buffer[offset + sampleCount + 0] = (float)(sampleValue * MathUtils.Clamp01(MathUtils.Map(Balance, -1, 1, 2, 0)));
            buffer[offset + sampleCount + 1] = (float)(sampleValue * MathUtils.Clamp01(MathUtils.Map(Balance, -1, 1, 0, 2)));
        }

        return count;

        double GetSampleValue()
        {
            if (Type == SignalGeneratorType.Sin)
                return Amplitude * Math.Sin(Math.PI * _phase);
            else if (Type == SignalGeneratorType.Square)
                return _phase % 2 - 1 >= 0 ? Amplitude : -Amplitude;
            else if (Type == SignalGeneratorType.SawTooth)
                return Amplitude * (_phase % 2 - 1);
            else if (Type == SignalGeneratorType.Triangle)
            {
                var value = 2 * (_phase % 2);
                if (value > 1) value = 2 - value;
                if (value < -1) value = -2 - value;

                return Amplitude * value;
            }

            return 0.0;
        }
    }
}

public enum SignalGeneratorType
{
    Sin,
    Square,
    Triangle,
    SawTooth,
}