using Microsoft.WindowsAPICodePack.Dialogs;
using MultiFunPlayer.Common;
using MultiFunPlayer.UI;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.IO;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("File")]
public class FileOutputTargetViewModel : ThreadAbstractOutputTarget
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override ConnectionStatus Status { get; protected set; }

    public DirectoryInfo OutputDirectory { get; set; } = null;
    public ScriptType ScriptType { get; set; } = ScriptType.Funscript;
    public ObservableConcurrentCollection<DeviceAxis> EnabledAxes { get; set; }

    public FileOutputTargetViewModel(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        : base(instanceIndex, eventAggregator, valueProvider)
    {
        EnabledAxes = new ObservableConcurrentCollection<DeviceAxis>();
        EnabledAxes.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == "Count")
                NotifyOfPropertyChange(nameof(CanToggleConnect));
        };

        UpdateInterval = 20;
    }

    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy && EnabledAxes.Count > 0;

    protected override void Run(CancellationToken token)
    {
        var writers = new Dictionary<DeviceAxis, IScriptWriter>();

        try
        {
            Logger.Info("Connecting to {0}", Identifier);
            if (EnabledAxes.Count == 0)
                throw new Exception("At least one axis must be enabled");

            if (OutputDirectory?.AsRefreshed().Exists != true)
                throw new DirectoryNotFoundException("Output directory does not exist");

            var baseFileName = $"MultiFunPlayer_{DateTime.Now:yyyyMMddTHHmmss}";
            foreach (var axis in EnabledAxes)
            {
                writers[axis] = ScriptType switch
                {
                    ScriptType.Funscript => new FunscriptWriter(Path.Join(OutputDirectory.FullName, $"{baseFileName}.{axis}.funscript")),
                    ScriptType.Csv => new CsvWriter(Path.Join(OutputDirectory.FullName, $"{baseFileName}.{axis}.csv")),
                    _ => throw new NotSupportedException()
                };
            }

            Status = ConnectionStatus.Connected;
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error when initializing writers");
            _ = DialogHelper.ShowErrorAsync(e, "Error when initializing writers", "RootDialog");
            return;
        }

        try
        {
            var currentTime = 0d;
            var lastSavedValues = DeviceAxis.All.ToDictionary(a => a, _ => double.NaN);
            FixedUpdate(() => !token.IsCancellationRequested, elapsed =>
            {
                Logger.Trace("Begin FixedUpdate [Elapsed: {0}]", elapsed);
                UpdateValues();

                currentTime += elapsed;
                foreach (var axis in EnabledAxes.Where(x => DeviceAxis.IsValueDirty(Values[x], lastSavedValues[x], 1E-10)))
                {
                    writers[axis].Write(currentTime, Values[axis]);
                    lastSavedValues[axis] = Values[axis];
                }
            });
        }
        catch (Exception e)
        {
            Logger.Error(e, $"{Identifier} failed with exception");
            _ = DialogHelper.ShowErrorAsync(e, $"{Identifier} failed with exception", "RootDialog");
        }

        foreach (var (_, writer) in writers)
        {
            try { writer.Dispose(); }
            catch (Exception e) { Logger.Warn(e, "Error disposing writer"); }
        }
    }

    public void OnSetOutputDirectory()
    {
        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = true
        };

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            return;

        OutputDirectory = new DirectoryInfo(dialog.FileName);
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
        {
            settings[nameof(OutputDirectory)] = OutputDirectory != null ? JToken.FromObject(OutputDirectory) : null;
            settings[nameof(ScriptType)] = new JValue(ScriptType);
            settings[nameof(EnabledAxes)] = JArray.FromObject(EnabledAxes);
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<DirectoryInfo>(nameof(OutputDirectory), out var outputDirectory))
                OutputDirectory = outputDirectory;
            if (settings.TryGetValue<ScriptType>(nameof(ScriptType), out var scriptType))
                ScriptType = scriptType;

            if (settings.TryGetValue<List<DeviceAxis>>(nameof(EnabledAxes), out var enabledAxes))
            {
                EnabledAxes.Clear();
                EnabledAxes.AddRange(enabledAxes);
            }
        }
    }

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token) => await ValueTask.FromResult(false);
}
