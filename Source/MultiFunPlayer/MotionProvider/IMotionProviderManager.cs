using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.MotionProvider.ViewModels;
using MultiFunPlayer.Settings;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Reflection;

namespace MultiFunPlayer.MotionProvider;

internal interface IMotionProviderManager : IDeviceAxisValueProvider
{
    IEnumerable<string> MotionProviderNames { get; }

    IMotionProvider GetMotionProvider(DeviceAxis axis, string motionProviderName);
    void Update(DeviceAxis axis, string motionProviderName, double deltaTime);
    void RegisterActions(IShortcutManager shortcutManager);
}

internal class MotionProviderManager : IMotionProviderManager, IHandle<SettingsMessage>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly IEventAggregator _eventAggregator;
    private readonly HashSet<string> _motionProviderNames;
    private readonly Dictionary<DeviceAxis, Dictionary<string, IMotionProvider>> _motionProviders;
    private readonly Dictionary<DeviceAxis, double> _values;

    public IEnumerable<string> MotionProviderNames => _motionProviderNames;

    public MotionProviderManager(IEventAggregator eventAggregator, IMotionProviderFactory motionProviderFactory)
    {
        _eventAggregator = eventAggregator;
        _eventAggregator.Subscribe(this);

        var motionProviderTypes = ReflectionUtils.FindImplementations<IMotionProvider>();
        _motionProviderNames = motionProviderTypes.Select(t => t.GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName)
                                                  .ToHashSet();
        _motionProviders = DeviceAxis.All.ToDictionary(a => a,
                                                       a => motionProviderFactory.CreateMotionProviderCollection(a)
                                                                                 .ToDictionary(p => p.GetType().GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName,
                                                                                               p => p));
        _values = DeviceAxis.All.ToDictionary(a => a, _ => double.NaN);
    }

    public IMotionProvider GetMotionProvider(DeviceAxis axis, string motionProviderName)
    {
        if (axis == null || motionProviderName == null)
            return null;

        var motionProviders = _motionProviders[axis];
        if (!motionProviders.TryGetValue(motionProviderName, out var motionProvider))
            return null;

        return motionProvider;
    }

    public T GetMotionProvider<T>(DeviceAxis axis) where T : class, IMotionProvider
    {
        var name = typeof(T).GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;
        return GetMotionProvider(axis, name) as T;
    }

    public void Update(DeviceAxis axis, string motionProviderName, double deltaTime)
    {
        var motionProvider = GetMotionProvider(axis, motionProviderName);
        if (motionProvider == null)
            return;

        motionProvider.Update(deltaTime);
        _values[axis] = motionProvider.Value;
    }

    public double GetValue(DeviceAxis axis) => _values[axis];

    public void Handle(SettingsMessage message)
    {
        if (message.Action == SettingsAction.Saving)
        {
            message.Settings["MotionProvider"] = JObject.FromObject(_motionProviders.ToDictionary(x => x.Key, x => x.Value.Values.Select(p => new TypedValue(p)).ToList()));
        }
        else if (message.Action == SettingsAction.Loading)
        {
            if (message.Settings.TryGetValue<Dictionary<DeviceAxis, List<JObject>>>("MotionProvider", out var motionProviderMap))
            {
                foreach (var (axis, motionProviderTokens) in motionProviderMap)
                {
                    foreach (var motionProviderToken in motionProviderTokens)
                    {
                        var type = motionProviderToken.GetTypeProperty();
                        var provider = _motionProviders[axis].Values.FirstOrDefault(p => p.GetType() == type);
                        if (provider == null)
                        {
                            Logger.Warn("Could not find provider with type \"{0}\"", type);
                            continue;
                        }

                        motionProviderToken.Populate(provider);
                    }
                }
            }
        }
    }

    public void RegisterActions(IShortcutManager s)
    {
        CustomCurveMotionProviderViewModel.RegisterActions(s, GetMotionProvider<CustomCurveMotionProviderViewModel>);
        LoopingScriptMotionProviderViewModel.RegisterActions(s, GetMotionProvider<LoopingScriptMotionProviderViewModel>);
        PatternMotionProviderViewModel.RegisterActions(s, GetMotionProvider<PatternMotionProviderViewModel>);
        RandomMotionProviderViewModel.RegisterActions(s, GetMotionProvider<RandomMotionProviderViewModel>);
    }
}