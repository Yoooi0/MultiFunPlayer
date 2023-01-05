using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using MultiFunPlayer.MotionProvider.ViewModels;
using MultiFunPlayer.Settings;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.IO;
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
        static void UpdateProperty<T>(T motionProvicer, Action<T> callback) where T : IMotionProvider
        {
            if (motionProvicer != null)
                callback(motionProvicer);
        }

        foreach (var type in ReflectionUtils.FindImplementations<IMotionProvider>())
        {
            var name = type.GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName;

            #region MotionProvider::Speed
            s.RegisterAction<DeviceAxis, double>($"MotionProvider::{name}::Speed::Offset",
            s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
            s => s.WithLabel("Value offset").WithStringFormat("{}{0}%"),
            (axis, offset) => UpdateProperty(GetMotionProvider(axis, name), p => p.Speed = Math.Max(0.01, p.Speed + offset / 100)));

            s.RegisterAction<DeviceAxis, double>($"MotionProvider::{name}::Speed::Set",
                s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
                s => s.WithLabel("Value").WithStringFormat("{}{0}%"),
                (axis, value) => UpdateProperty(GetMotionProvider(axis, name), p => p.Speed = Math.Max(0.01, value / 100)));

            s.RegisterAction<IAxisInputGesture, DeviceAxis>($"MotionProvider::{name}::Speed::Drive",
                s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
                (gesture, axis) => UpdateProperty(GetMotionProvider(axis, name), p => p.Speed = Math.Max(0.01, p.Speed + gesture.Delta)));
            #endregion

            #region MotionProvider::Minimum
            s.RegisterAction<DeviceAxis, double>($"MotionProvider::{name}::Minimum::Offset",
                s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
                s => s.WithLabel("Value offset").WithStringFormat("{}{0}%"),
                (axis, offset) => UpdateProperty(GetMotionProvider(axis, name), p => p.Minimum = Math.Clamp(p.Minimum + offset, 0, 100)));

            s.RegisterAction<DeviceAxis, double>($"MotionProvider::{name}::Minimum::Set",
                s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
                s => s.WithLabel("Value").WithStringFormat("{}{0}%"),
                (axis, value) => UpdateProperty(GetMotionProvider(axis, name), p => p.Minimum = Math.Clamp(value, 0, 100)));

            s.RegisterAction<IAxisInputGesture, DeviceAxis>($"MotionProvider::{name}::Minimum::Drive",
                s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
                (gesture, axis) => UpdateProperty(GetMotionProvider(axis, name), p => p.Minimum = Math.Clamp(p.Minimum + gesture.Delta, 0, 100)));
            #endregion

            #region MotionProvider::Maximum
            s.RegisterAction<DeviceAxis, double>($"MotionProvider::{name}::Maximum::Offset",
                s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
                s => s.WithLabel("Value offset").WithStringFormat("{}{0}%"),
                (axis, offset) => UpdateProperty(GetMotionProvider(axis, name), p => p.Maximum = Math.Clamp(p.Maximum + offset, 0, 100)));

            s.RegisterAction<DeviceAxis, double>($"MotionProvider::{name}::Maximum::Set",
                s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
                s => s.WithLabel("Value").WithStringFormat("{}{0}%"),
                (axis, value) => UpdateProperty(GetMotionProvider(axis, name), p => p.Maximum = Math.Clamp(value, 0, 100)));

            s.RegisterAction<IAxisInputGesture, DeviceAxis>($"MotionProvider::{name}::Maximum::Drive",
                s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
                (gesture, axis) => UpdateProperty(GetMotionProvider(axis, name), p => p.Maximum = Math.Clamp(p.Maximum + gesture.Delta, 0, 100)));
            #endregion

            #region PatternMotionProvider
            if (type == typeof(PatternMotionProviderViewModel))
            {
                #region PatternMotionProvider::Pattern
                s.RegisterAction<DeviceAxis, PatternType>($"MotionProvider::{name}::Pattern::Set",
                    s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
                    s => s.WithLabel("Pattern").WithItemsSource(Enum.GetValues<PatternType>()),
                    (axis, pattern) => UpdateProperty(GetMotionProvider<PatternMotionProviderViewModel>(axis), p => p.Pattern = pattern));
                #endregion
            }
            #endregion

            #region RandomMotionProvider
            if (type == typeof(RandomMotionProviderViewModel))
            {
                #region RandomMotionProvider::Octaves
                s.RegisterAction<DeviceAxis, int>($"MotionProvider::{name}::Octaves::Set",
                    s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
                    s => s.WithLabel("Octaves"),
                    (axis, octaves) => UpdateProperty(GetMotionProvider<RandomMotionProviderViewModel>(axis), p => p.Octaves = octaves));
                #endregion

                #region RandomMotionProvider::Persistence
                s.RegisterAction<DeviceAxis, double>($"MotionProvider::{name}::Persistence::Set",
                    s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
                    s => s.WithLabel("Persistence"),
                    (axis, persistence) => UpdateProperty(GetMotionProvider<RandomMotionProviderViewModel>(axis), p => p.Persistence = persistence));
                #endregion

                #region RandomMotionProvider::Lacunarity
                s.RegisterAction<DeviceAxis, double>($"MotionProvider::{name}::Lacunarity::Set",
                   s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
                   s => s.WithLabel("Lacunarity"),
                   (axis, lacunarity) => UpdateProperty(GetMotionProvider<RandomMotionProviderViewModel>(axis), p => p.Lacunarity = lacunarity));
                #endregion
            }
            #endregion

            #region LoopingScriptMotionProvider
            if (type == typeof(LoopingScriptMotionProviderViewModel))
            {
                #region LoopingMotionProvider::Script
                s.RegisterAction<DeviceAxis, string>($"MotionProvider::{name}::Script::Set",
                    s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
                    s => s.WithLabel("Script path"),
                    (axis, script) => UpdateProperty(GetMotionProvider<LoopingScriptMotionProviderViewModel>(axis), p => p.SourceFile = new FileInfo(script)));
                #endregion

                #region LoopingMotionProvider::Interpolation
                s.RegisterAction<DeviceAxis, InterpolationType>($"MotionProvider::{name}::Interpolation::Set",
                    s => s.WithLabel("Target axis").WithItemsSource(DeviceAxis.All),
                    s => s.WithLabel("Interpolation type").WithItemsSource(Enum.GetValues<InterpolationType>()),
                    (axis, interpolation) => UpdateProperty(GetMotionProvider<LoopingScriptMotionProviderViewModel>(axis), p => p.InterpolationType = interpolation));
                #endregion
            }
            #endregion
        }
    }
}