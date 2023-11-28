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

internal sealed class PluginContainer(FileInfo pluginFile) : PropertyChangedBase, IDisposable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private PluginCompilationResult _compilationResult;
    private CancellationTokenSource _cancellationSource;
    private Thread _thread;

    public FileInfo PluginFile { get; } = pluginFile;
    public Exception Exception { get; private set; }
    public PluginState State { get; private set; } = PluginState.Idle;

    public string Name => Path.GetFileNameWithoutExtension(PluginFile.Name);
    public UIElement SettingsView => _compilationResult?.SettingsView;

    public bool CanStart => State == PluginState.Idle || State == PluginState.RanToCompletion || (State == PluginState.Faulted && Exception is not PluginCompileException);
    public bool CanStop => State == PluginState.Running;
    public bool CanCompile => State == PluginState.Idle || State == PluginState.Faulted || State == PluginState.RanToCompletion;
    public bool IsBusy => State != PluginState.Idle && State != PluginState.RanToCompletion && State != PluginState.Running;

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

            plugin = _compilationResult.CreatePluginInstance();
            plugin.InternalInitialize(_cancellationSource.Token);

            State = PluginState.Running;

            try
            {
                if (plugin is SyncPluginBase syncPlugin)
                {
                    syncPlugin.InternalExecute();
                }
                else if (plugin is AsyncPluginBase asyncPlugin)
                {
                    // https://stackoverflow.com/a/9343733 ¯\_(ツ)_/¯
                    var task = asyncPlugin.InternalExecuteAsync();
                    task.GetAwaiter().GetResult();
                }
            }
            catch (OperationCanceledException) { }

            Logger.Debug($"\"{Name}\" ran to completion");
        }
        catch (Exception e)
        {
            Logger.Error(e, $"{Name} failed with exception");
            Exception = e;
        }
        finally
        {
            State = PluginState.Stopping;

            try
            {
                plugin?.InternalDispose();
                HandleSettings(SettingsAction.Saving);
            }
            catch (Exception e)
            {
                Exception = Exception == null ? e : new AggregateException(Exception, e);
            }

            State = Exception != null ? PluginState.Faulted : PluginState.RanToCompletion;

            _cancellationSource?.Dispose();
            _cancellationSource = null;
            _thread = null;
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

    private void Dispose(bool disposing)
    {
        _cancellationSource?.Cancel();
        _thread?.Join();

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
