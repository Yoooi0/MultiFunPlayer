using MultiFunPlayer.Common;
using MultiFunPlayer.MediaSource.MediaResource;
using NLog;
using PropertyChanged;
using Stylet;
using System.ComponentModel;
using System.Reflection;

namespace MultiFunPlayer.Script.Repository;

internal interface IScriptRepositoryManager
{
    IReadOnlyCollection<IScriptRepository> Repositories { get; }

    void BeginSearchForScripts(MediaResourceInfo mediaResource, IEnumerable<DeviceAxis> axes, Action<Dictionary<DeviceAxis, IScriptResource>> callback, CancellationToken token);
    Task<Dictionary<DeviceAxis, IScriptResource>> SearchForScriptsAsync(MediaResourceInfo mediaResource, IEnumerable<DeviceAxis> axes, CancellationToken token);
}

internal sealed class ScriptRepositoryManager : Screen, IScriptRepositoryManager, IHandle<SettingsMessage>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly IEventAggregator _eventAggregator;
    private readonly ILocalScriptRepository _localRepository;

    public IReadOnlyCollection<IScriptRepository> Repositories { get; }

    public ScriptRepositoryManager(IEnumerable<IScriptRepository> repositories, IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator;
        _eventAggregator.Subscribe(this);

        _localRepository = repositories.Single(r => r.GetType().IsAssignableTo(typeof(ILocalScriptRepository))) as ILocalScriptRepository;
        Repositories = [.. repositories.OrderBy(r => r.GetType().GetCustomAttribute<DisplayIndexAttribute>()?.Index ?? int.MaxValue)];

        foreach (var repository in Repositories)
            repository.PropertyChanged += OnRepositoryPropertyChanged;
    }

    public void BeginSearchForScripts(MediaResourceInfo mediaResource, IEnumerable<DeviceAxis> axes, Action<Dictionary<DeviceAxis, IScriptResource>> callback, CancellationToken token)
    {
        _eventAggregator.Publish(new PreScriptSearchMessage(mediaResource));
        _ = Task.Run(async () =>
        {
            var result = await SearchForScriptsAsync(mediaResource, axes, token);
            callback(result);
            _eventAggregator.Publish(new PostScriptSearchMessage(mediaResource, result));
        }, token);
    }

    public async Task<Dictionary<DeviceAxis, IScriptResource>> SearchForScriptsAsync(MediaResourceInfo mediaResource, IEnumerable<DeviceAxis> axes, CancellationToken token)
    {
        var result = new Dictionary<DeviceAxis, IScriptResource>();
        if (mediaResource == null)
            return result;

        Logger.Info("Trying to match scripts to media [Name: \"{0}\", Source: \"{1}\"]", mediaResource.Name, mediaResource.Source);
        foreach (var repository in Repositories)
        {
            if (!repository.Enabled)
                continue;

            try
            {
                Logger.Debug("Searching for scripts in {0} repository", repository.Name);
                result.Merge(await repository.SearchForScriptsAsync(mediaResource, axes, _localRepository, token));
            }
            catch (Exception e)
            {
                Logger.Error(e, "{0} repository failed with exception", repository.Name);
            }
        }

        foreach (var (axis, resource) in result)
            Logger.Info("Matched {0} script to [Name: \"{1}\", Source: \"{2}\"]", axis, resource?.Name, resource?.Source);

        return result;
    }

    [SuppressPropertyChangedWarnings]
    private void OnRepositoryPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IScriptRepository.Enabled))
            _eventAggregator.Publish(new ReloadScriptsRequestMessage());
    }

    public void Handle(SettingsMessage message)
    {
        if (message.Action == SettingsAction.Saving)
        {
            if (!message.Settings.EnsureContainsObjects("Script", "Repositories")
             || !message.Settings.TryGetObject(out var settings, "Script", "Repositories"))
                return;

            foreach (var repository in Repositories)
            {
                if (!settings.EnsureContainsObjects(repository.Name)
                 || !settings.TryGetObject(out var repositorySettings, repository.Name))
                    continue;

                repository.HandleSettings(repositorySettings, message.Action);
            }
        }
        else if (message.Action == SettingsAction.Loading)
        {
            if (!message.Settings.TryGetObject(out var settings, "Script", "Repositories"))
                return;

            foreach (var repository in Repositories)
            {
                if (!settings.TryGetObject(out var repositorySettings, repository.Name))
                    continue;

                repository.HandleSettings(repositorySettings, message.Action);
            }
        }
    }
}