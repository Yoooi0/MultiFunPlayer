using Microsoft.Win32;
using MultiFunPlayer.Common;
using MultiFunPlayer.Script;
using MultiFunPlayer.UI;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.IO;

namespace MultiFunPlayer.OutputTarget.ViewModels;

[DisplayName("File")]
internal sealed class FileOutputTarget(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
    : ThreadAbstractOutputTarget(instanceIndex, eventAggregator, valueProvider)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override ConnectionStatus Status { get; protected set; }
    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsDisconnected => Status == ConnectionStatus.Disconnected;
    public bool IsConnectBusy => Status == ConnectionStatus.Connecting || Status == ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy;

    public DirectoryInfo OutputDirectory { get; set; } = null;
    public ScriptType ScriptType { get; set; } = ScriptType.Funscript;

    protected override IUpdateContext RegisterUpdateContext(DeviceAxisUpdateType updateType) => updateType switch
    {
        DeviceAxisUpdateType.FixedUpdate => new ThreadFixedUpdateContext() { UpdateInterval = 20 },
        _ => null,
    };

    protected override void Run(CancellationToken token)
    {
        var writers = new Dictionary<DeviceAxis, IScriptWriter>();

        try
        {
            Logger.Info("Connecting to {0}", Identifier);
            if (!AxisSettings.Values.Any(x => x.Enabled))
                throw new Exception("At least one axis must be enabled");

            if (OutputDirectory?.AsRefreshed().Exists != true)
                throw new DirectoryNotFoundException("Output directory does not exist");

            var baseFileName = $"MultiFunPlayer_{DateTime.Now:yyyyMMddTHHmmss}";
            foreach (var axis in AxisSettings.Where(x => x.Value.Enabled).Select(x => x.Key))
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
            var currentValues = DeviceAxis.All.ToDictionary(a => a, _ => double.NaN);
            var lastSavedValues = DeviceAxis.All.ToDictionary(a => a, _ => double.NaN);
            FixedUpdate(() => !token.IsCancellationRequested, (_, elapsed) =>
            {
                Logger.Trace("Begin FixedUpdate [Elapsed: {0}]", elapsed);
                GetValues(currentValues);

                currentTime += elapsed;

                var values = currentValues.Where(x => DeviceAxis.IsValueDirty(x.Value, lastSavedValues[x.Key], 1E-10));
                values = values.Where(x => AxisSettings[x.Key].Enabled);

                foreach (var (axis, value) in values)
                {
                    writers[axis].Write(currentTime, value);
                    lastSavedValues[axis] = value;
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
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() != true)
            return;

        OutputDirectory = new DirectoryInfo(dialog.FolderName);
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
        {
            settings[nameof(OutputDirectory)] = OutputDirectory != null ? JToken.FromObject(OutputDirectory) : null;
            settings[nameof(ScriptType)] = new JValue(ScriptType);
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<DirectoryInfo>(nameof(OutputDirectory), out var outputDirectory))
                OutputDirectory = outputDirectory;
            if (settings.TryGetValue<ScriptType>(nameof(ScriptType), out var scriptType))
                ScriptType = scriptType;
        }
    }

    public override async ValueTask<bool> CanConnectAsync(CancellationToken token) => await ValueTask.FromResult(false);
}
