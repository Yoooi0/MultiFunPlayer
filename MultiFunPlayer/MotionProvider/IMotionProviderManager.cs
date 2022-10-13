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

public interface IMotionProviderManager : IDeviceAxisValueProvider
{
    IEnumerable<string> MotionProviderNames { get; }

    IMotionProvider GetMotionProvider(DeviceAxis axis, string motionProviderName);
    void Update(DeviceAxis axis, string motionProviderName, double deltaTime);
    void RegisterShortcuts(IShortcutManager shortcutManager);
}

public class MotionProviderManager : IMotionProviderManager, IHandle<SettingsMessage>
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

    public void RegisterShortcuts(IShortcutManager s)
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
            s.RegisterAction($"MotionProvider::{name}::Speed::Offset",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value offset").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, offset) =>
                      UpdateProperty(GetMotionProvider(axis, name), p => p.Speed = Math.Max(0.01, p.Speed + offset / 100))
                  ));

            s.RegisterAction($"MotionProvider::{name}::Speed::Set",
                b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                      .WithSetting<float>(p => p.WithLabel("Value").WithStringFormat("{}{0}%"))
                      .WithCallback((_, axis, value) =>
                          UpdateProperty(GetMotionProvider(axis, name), p => p.Speed = Math.Max(0.01, value / 100))
                      ));

            s.RegisterAction($"MotionProvider::{name}::Speed::Drive",
                b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                      .WithCallback((gesture, axis) =>
                      {
                          if (gesture is IAxisInputGesture axisGesture)
                              UpdateProperty(GetMotionProvider(axis, name), p => p.Speed = Math.Max(0.01, p.Speed + axisGesture.Delta));
                      }), ShortcutActionDescriptorFlags.AcceptsAxisGesture);
            #endregion

            #region MotionProvider::Minimum
            s.RegisterAction($"MotionProvider::{name}::Minimum::Offset",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value offset").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, offset) =>
                      UpdateProperty(GetMotionProvider(axis, name), p => p.Minimum = MathUtils.Clamp(p.Minimum + offset, 0, 100))
                  ));

            s.RegisterAction($"MotionProvider::{name}::Minimum::Set",
                b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                      .WithSetting<float>(p => p.WithLabel("Value").WithStringFormat("{}{0}%"))
                      .WithCallback((_, axis, value) =>
                          UpdateProperty(GetMotionProvider(axis, name), p => p.Minimum = MathUtils.Clamp(value, 0, 100))
                      ));

            s.RegisterAction($"MotionProvider::{name}::Minimum::Drive",
                b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                      .WithCallback((gesture, axis) =>
                      {
                          if (gesture is IAxisInputGesture axisGesture)
                              UpdateProperty(GetMotionProvider(axis, name), p => p.Minimum = MathUtils.Clamp(p.Minimum + axisGesture.Delta, 0, 100));
                      }), ShortcutActionDescriptorFlags.AcceptsAxisGesture);
            #endregion

            #region MotionProvider::Maximum
            s.RegisterAction($"MotionProvider::{name}::Maximum::Offset",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value offset").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, offset) =>
                      UpdateProperty(GetMotionProvider(axis, name), p => p.Maximum = MathUtils.Clamp(p.Maximum + offset, 0, 100))
                  ));

            s.RegisterAction($"MotionProvider::{name}::Maximum::Set",
                b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                      .WithSetting<float>(p => p.WithLabel("Value").WithStringFormat("{}{0}%"))
                      .WithCallback((_, axis, value) =>
                          UpdateProperty(GetMotionProvider(axis, name), p => p.Maximum = MathUtils.Clamp(value, 0, 100))
                      ));

            s.RegisterAction($"MotionProvider::{name}::Maximum::Drive",
                b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                      .WithCallback((gesture, axis) =>
                      {
                          if (gesture is IAxisInputGesture axisGesture)
                              UpdateProperty(GetMotionProvider(axis, name), p => p.Maximum = MathUtils.Clamp(p.Maximum + axisGesture.Delta, 0, 100));
                      }), ShortcutActionDescriptorFlags.AcceptsAxisGesture);
            #endregion

            #region PatternMotionProvider
            if (type == typeof(PatternMotionProviderViewModel))
            {
                #region PatternMotionProvider::Pattern
                s.RegisterAction($"MotionProvider::{name}::Pattern::Set",
                    b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                          .WithSetting<PatternType>(p => p.WithLabel("Pattern").WithItemsSource(Enum.GetValues<PatternType>()))
                          .WithCallback((_, axis, pattern) =>
                              UpdateProperty(GetMotionProvider<PatternMotionProviderViewModel>(axis), p => p.Pattern = pattern)
                          ));
                #endregion
            }
            #endregion

            #region RandomMotionProvider
            if (type == typeof(RandomMotionProviderViewModel))
            {
                #region RandomMotionProvider::Octaves
                s.RegisterAction($"MotionProvider::{name}::Octaves::Set",
                    b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                          .WithSetting<int>(p => p.WithLabel("Octaves"))
                          .WithCallback((_, axis, octaves) =>
                              UpdateProperty(GetMotionProvider<RandomMotionProviderViewModel>(axis), p => p.Octaves = octaves)
                          ));
                #endregion

                #region RandomMotionProvider::Persistence
                s.RegisterAction($"MotionProvider::{name}::Persistence::Set",
                    b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                          .WithSetting<float>(p => p.WithLabel("Persistence"))
                          .WithCallback((_, axis, persistence) =>
                              UpdateProperty(GetMotionProvider<RandomMotionProviderViewModel>(axis), p => p.Persistence = persistence)
                          ));
                #endregion

                #region RandomMotionProvider::Lacunarity
                s.RegisterAction($"MotionProvider::{name}::Lacunarity::Set",
                    b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                          .WithSetting<float>(p => p.WithLabel("Lacunarity"))
                          .WithCallback((_, axis, lacunarity) =>
                              UpdateProperty(GetMotionProvider<RandomMotionProviderViewModel>(axis), p => p.Lacunarity = lacunarity)
                          ));
                #endregion
            }
            #endregion

            #region LoopingScriptMotionProvider
            if (type == typeof(LoopingScriptMotionProviderViewModel))
            {
                #region LoopingMotionProvider::Script
                s.RegisterAction($"MotionProvider::{name}::Script::Set",
                    b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                          .WithSetting<string>(p => p.WithLabel("Script path"))
                          .WithCallback((_, axis, script) =>
                              UpdateProperty(GetMotionProvider<LoopingScriptMotionProviderViewModel>(axis), p => p.SourceFile = new FileInfo(script))
                          ));
                #endregion

                #region LoopingMotionProvider::Interpolation
                s.RegisterAction($"MotionProvider::{name}::Interpolation::Set",
                    b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                          .WithSetting<InterpolationType>(p => p.WithLabel("Interpolation type").WithItemsSource(Enum.GetValues<InterpolationType>()))
                          .WithCallback((_, axis, interpolation) =>
                              UpdateProperty(GetMotionProvider<LoopingScriptMotionProviderViewModel>(axis), p => p.InterpolationType = interpolation)
                          ));
                #endregion
            }
            #endregion
        }
    }
}