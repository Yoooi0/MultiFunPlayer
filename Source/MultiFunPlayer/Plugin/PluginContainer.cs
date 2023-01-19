using MultiFunPlayer.Common;
using MultiFunPlayer.Settings;
using NLog;
using Stylet;
using System.IO;
using System.Windows;

namespace MultiFunPlayer.Plugin;

internal enum PluginState
{
    Idle,
    Compiling,
    Starting,
    Running,
    Stopping,
    Faulted,
    RanToCompletion,
}

internal class PluginContainer : PropertyChangedBase, IDisposable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private PluginCompilationResult _compilationResult;
    private CancellationTokenSource _cancellationSource;
    private Thread _thread;

    public FileInfo PluginFile { get; }
    public Exception Exception { get; private set; }
    public PluginState State { get; private set; } = PluginState.Idle;

    public string Name => Path.GetFileNameWithoutExtension(PluginFile.Name);
    public UIElement SettingsView => _compilationResult?.SettingsView;

    public bool CanStart => State == PluginState.Idle || State == PluginState.RanToCompletion || (State == PluginState.Faulted && Exception is not PluginCompileException);
    public bool CanStop => State == PluginState.Running;
    public bool CanCompile => State == PluginState.Idle || State == PluginState.Faulted || State == PluginState.RanToCompletion;
    public bool IsBusy => State != PluginState.Idle && State != PluginState.RanToCompletion && State != PluginState.Running;

    public PluginContainer(FileInfo pluginFile)
    {
        PluginFile = pluginFile;
    }

    public void Start()
    {
        if (!CanStart)
            return;

        if (_compilationResult?.Success != true)
        {
            QueueCompile(() => {
                if (_compilationResult?.Success == true)
                    Start();
            });
            return;
        }

        State = PluginState.Starting;

        _cancellationSource = new CancellationTokenSource();
        _thread = new Thread(Execute) { IsBackground = true };
        _thread.Start();
    }

    private void Execute()
    {
        var plugin = default(PluginBase);
        try
        {
            Logger.Info($"Starting \"{Name}\"");

            var token = _cancellationSource.Token;
            token.Register(() => plugin?.InternalDispose());

            plugin = _compilationResult.CreatePluginInstance();
            plugin.InternalInitialize();

            State = PluginState.Running;

            try
            {
                if (plugin is SyncPluginBase syncPlugin)
                {
                    syncPlugin.InternalExecute(token);
                }
                else if (plugin is AsyncPluginBase asyncPlugin)
                {
                    // https://stackoverflow.com/a/9343733 ¯\_(ツ)_/¯
                    var task = asyncPlugin.InternalExecuteAsync(token);
                    task.GetAwaiter().GetResult();
                }
            }
            catch (OperationCanceledException) { }

            State = PluginState.Stopping;
            plugin.InternalDispose();
            State = PluginState.RanToCompletion;

            Logger.Debug($"\"{Name}\" ran to completion");
        }
        catch (Exception e)
        {
            Logger.Error(e, $"{Name} failed with exception");

            State = PluginState.Stopping;
            plugin?.InternalDispose();
            State = PluginState.Faulted;
            Exception = e;
        }
    }

    public void Stop()
    {
        if (!CanStop)
            return;

        _ = Task.Run(() =>
        {
            State = PluginState.Stopping;
            Dispose();
        });
    }

    public void Compile()
    {
        if (!CanCompile)
            return;

        QueueCompile();
    }

    private void QueueCompile(Action callback = null)
    {
        if (!PluginFile.Exists || State == PluginState.Compiling)
            return;

        State = PluginState.Compiling;

        var contents = File.ReadAllText(PluginFile.FullName);
        PluginCompiler.QueueCompile(PluginFile, x => {
            OnCompile(x);
            callback?.Invoke();
        });

        void OnCompile(PluginCompilationResult result)
        {
            if (_compilationResult != null)
            {
                State = PluginState.Stopping;
                Dispose();
                State = PluginState.Idle;
            }

            _compilationResult = result;
            if (_compilationResult.Success)
            {
                State = PluginState.Idle;
                Exception = null;
            }
            else
            {
                State = PluginState.Faulted;
                Exception = _compilationResult.Exception;
            }

            HandleSettings(SettingsAction.Loading);
            NotifyOfPropertyChange(nameof(SettingsView));
        }
    }

    public void HandleSettings(SettingsAction action)
    {
        if (_compilationResult == null || _compilationResult.Settings == null)
            return;

        var settingsPath = $"Plugins\\{Path.GetFileNameWithoutExtension(PluginFile.Name)}.config.json";
        var settings = SettingsHelper.ReadOrEmpty(settingsPath);
        _compilationResult.Settings.HandleSettings(settings, action);

        if (action == SettingsAction.Saving && settings.HasValues)
            SettingsHelper.Write(settings, settingsPath);
    }

    protected virtual void Dispose(bool disposing)
    {
        _cancellationSource?.Cancel();

        _thread?.Join();

        HandleSettings(SettingsAction.Saving);
        _cancellationSource?.Dispose();

        _compilationResult?.Dispose();

        _thread = null;
        _cancellationSource = null;
        _compilationResult = null;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
