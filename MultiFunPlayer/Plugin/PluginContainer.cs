using MultiFunPlayer.Common;
using NLog;
using Stylet;
using System.IO;

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

    private PluginCompilationResult _compileResult;
    private CancellationTokenSource _cancellationSource;
    private Thread _thread;
    private IPlugin _plugin;

    public FileInfo PluginFile { get; }
    public Exception Exception { get; private set; }

    public bool CanStart => State == PluginState.Idle || State == PluginState.RanToCompletion;
    public bool CanStop => State == PluginState.Running;
    public bool CanCompile => State == PluginState.Idle || State == PluginState.Faulted || State == PluginState.RanToCompletion;

    public PluginState State { get; private set; } = PluginState.Idle;
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
            if (_plugin is ISyncPlugin syncPlugin)
            {
                syncPlugin.Execute(token);
            }
            else if (_plugin is IAsyncPlugin asyncPlugin)
            {
                var handle = new ManualResetEvent(false);
                var task = Task.Factory.StartNew(async () => {
                    try { await asyncPlugin.ExecuteAsync(token); }
                    catch (Exception e) { e.Throw(); }
                    finally { handle.Set(); }
                }).Unwrap();

                handle.WaitOne();
                task.ThrowIfFaulted();
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

        _thread = null;
        _cancellationSource = null;
        _plugin = null;

        State = PluginState.Idle;
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
            _compileResult?.Dispose();
            _compileResult = result;
            if (_plugin != null)
                Stop();

            if (_compileResult.Success)
            {
                State = PluginState.Idle;

                Exception = null;
                _plugin = _compileResult.Instance;
            }
            else
            {
                State = PluginState.Faulted;

                Exception = _compileResult.Exception;
                _plugin = null;
            }

            NotifyOfPropertyChange(nameof(CanStart));
            NotifyOfPropertyChange(nameof(CanStop));
            NotifyOfPropertyChange(nameof(CanCompile));
        }
    }

    protected virtual void Dispose(bool disposing) => Stop();

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
