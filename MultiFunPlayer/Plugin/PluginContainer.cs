using MultiFunPlayer.Common;
using MultiFunPlayer.Settings;
using NLog;
using Stylet;
using System.IO;
using System.Windows;

namespace MultiFunPlayer.Plugin;

public enum PluginState
{
    Idle,
    Compiling,
    Starting,
    Running,
    Stopping,
    Faulted,
    RanToCompletion,
}

public class PluginContainer : PropertyChangedBase, IDisposable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private PluginCompilationResult _compilationResult;
    private CancellationTokenSource _cancellationSource;
    private Thread _thread;
    private PluginBase _plugin;

    public FileInfo PluginFile { get; }
    public Exception Exception { get; private set; }
    public PluginState State { get; private set; } = PluginState.Idle;

    public UIElement View => _compilationResult?.View;

    public bool CanStart => State == PluginState.Idle || State == PluginState.RanToCompletion;
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

        if (_plugin == null)
        {
            QueueCompile(() => {
                if (_plugin != null)
                    Start();
            });
            return;
        }

        State = PluginState.Starting;

        _cancellationSource = new CancellationTokenSource();
        _thread = new Thread(Execute) { IsBackground = true };
        _thread.Start();
    }

    public void Compile()
    {
        if (!CanCompile)
            return;

        QueueCompile();
    }

    private void Execute()
    {
        try
        {
            Logger.Info($"Starting \"{_plugin.Name}\"");

            State = PluginState.Running;

            var token = _cancellationSource.Token;
            if (_plugin is SyncPluginBase syncPlugin)
            {
                syncPlugin.Execute(token);
            }
            else if (_plugin is AsyncPluginBase asyncPlugin)
            {
                // https://stackoverflow.com/a/9343733 ¯\_(ツ)_/¯
                var task = asyncPlugin.ExecuteAsync(token);
                task.GetAwaiter().GetResult();
            }

            State = PluginState.RanToCompletion;

            Logger.Debug($"\"{_plugin.Name}\" ran to completion");
        }
        catch (Exception e)
        {
            Logger.Error(e, $"{_plugin.Name} failed with exception");

            State = PluginState.Faulted;
            Exception = e;
        }
    }

    public void Stop()
    {
        if (!CanStop)
            return;

        State = PluginState.Stopping;

        _cancellationSource?.Cancel();
        _thread?.Join();
        _cancellationSource?.Dispose();

        HandleSettings(SettingsAction.Saving);

        _thread = null;
        _cancellationSource = null;
        _plugin = null;

        State = PluginState.Idle;
    }

    public void HandleSettings(SettingsAction action)
    {
        if (_plugin == null)
            return;

        var settingsPath = $"Plugins\\{Path.GetFileNameWithoutExtension(PluginFile.Name)}.config.json";
        var settings = SettingsHelper.ReadOrEmpty(settingsPath);
        _plugin.HandleSettings(settings, action);

        if (action == SettingsAction.Saving && settings.HasValues)
            SettingsHelper.Write(settings, settingsPath);
    }

    private void QueueCompile(Action callback = null)
    {
        if (!PluginFile.Exists || State == PluginState.Compiling)
            return;

        State = PluginState.Compiling;

        var contents = File.ReadAllText(PluginFile.FullName);
        PluginCompiler.QueueCompile(contents, x => {
            OnCompile(x);
            callback?.Invoke();
        });

        void OnCompile(PluginCompilationResult result)
        {
            if (_plugin != null)
                Stop();

            _compilationResult?.Dispose();
            _compilationResult = result;
            if (_compilationResult.Success)
            {
                State = PluginState.Idle;

                Exception = null;
                _plugin = _compilationResult.Instance;
                HandleSettings(SettingsAction.Loading);
            }
            else
            {
                State = PluginState.Faulted;

                Exception = _compilationResult.Exception;
                _plugin = null;
            }

            NotifyOfPropertyChange(nameof(View));
        }
    }

    protected virtual void Dispose(bool disposing) => Stop();

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
