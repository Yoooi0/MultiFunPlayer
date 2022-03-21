using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.Input;
using MultiFunPlayer.Settings;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System.ComponentModel;
using System.Reflection;

namespace MultiFunPlayer.MotionProvider;

public interface IMotionProviderManager : IDeviceAxisValueProvider
{
    IEnumerable<string> MotionProviderNames { get; }

    IMotionProvider GetMotionProvider(DeviceAxis axis, string motionProviderName);
    void Update(DeviceAxis axis, string motionProviderName, float deltaTime);
    void RegisterShortcuts(IShortcutManager shortcutManager);
}

public class MotionProviderManager : IMotionProviderManager, IHandle<AppSettingsMessage>
{
    protected Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly IEventAggregator _eventAggregator;
    private readonly HashSet<string> _motionProviderNames;
    private readonly Dictionary<DeviceAxis, Dictionary<string, IMotionProvider>> _motionProviders;
    private readonly Dictionary<DeviceAxis, float> _values;

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
        _values = DeviceAxis.All.ToDictionary(a => a, a => float.NaN);
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

    public void Update(DeviceAxis axis, string motionProviderName, float deltaTime)
    {
        var motionProvider = GetMotionProvider(axis, motionProviderName);
        if (motionProvider == null)
            return;

        motionProvider.Update(deltaTime);
        _values[axis] = motionProvider.Value;
    }

    public float GetValue(DeviceAxis axis) => _values[axis];

    public void Handle(AppSettingsMessage message)
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
        foreach(var name in MotionProviderNames)
        {
            #region MotionProvider::Speed
            s.RegisterAction($"MotionProvider::{name}::Speed::Offset",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value offset").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, offset) =>
                  {
                      var motionProvider = GetMotionProvider(axis, name);
                      if (motionProvider == null)
                          return;

                      motionProvider.Speed = MathF.Max(0.01f, motionProvider.Speed + offset / 100);
                  }));

            s.RegisterAction($"MotionProvider::{name}::Speed::Set",
                b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                      .WithSetting<float>(p => p.WithLabel("Value").WithStringFormat("{}{0}%"))
                      .WithCallback((_, axis, value) =>
                      {
                          var motionProvider = GetMotionProvider(axis, name);
                          if (motionProvider == null)
                              return;

                          motionProvider.Speed = MathF.Max(0.01f, value / 100);
                      }));

            s.RegisterAction($"MotionProvider::{name}::Speed::Drive",
                b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                      .WithCallback((gesture, axis) =>
                      {
                          if (gesture is not IAxisInputGesture axisGesture) 
                              return;

                          var motionProvider = GetMotionProvider(axis, name);
                          if (motionProvider == null)
                              return;

                          motionProvider.Speed = MathF.Max(0.01f, motionProvider.Speed + axisGesture.Delta);
                      }), ShortcutActionDescriptorFlags.AcceptsAxisGesture);
            #endregion

            #region MotionProvider::Minimum
            s.RegisterAction($"MotionProvider::{name}::Minimum::Offset",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value offset").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, offset) =>
                  {
                      var motionProvider = GetMotionProvider(axis, name);
                      if (motionProvider == null)
                          return;

                      motionProvider.Minimum = MathUtils.Clamp(motionProvider.Minimum + offset, 0, 100);
                  }));

            s.RegisterAction($"MotionProvider::{name}::Minimum::Set",
                b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                      .WithSetting<float>(p => p.WithLabel("Value").WithStringFormat("{}{0}%"))
                      .WithCallback((_, axis, value) =>
                      {
                          var motionProvider = GetMotionProvider(axis, name);
                          if (motionProvider == null)
                              return;

                          motionProvider.Minimum = MathUtils.Clamp(value, 0, 100);
                      }));

            s.RegisterAction($"MotionProvider::{name}::Minimum::Drive",
                b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                      .WithCallback((gesture, axis) =>
                      {
                          if (gesture is not IAxisInputGesture axisGesture)
                              return;

                          var motionProvider = GetMotionProvider(axis, name);
                          if (motionProvider == null)
                              return;

                          motionProvider.Minimum = MathUtils.Clamp(motionProvider.Minimum + axisGesture.Delta, 0, 100);
                      }), ShortcutActionDescriptorFlags.AcceptsAxisGesture);
            #endregion

            #region MotionProvider::Maximum
            s.RegisterAction($"MotionProvider::{name}::Maximum::Offset",
            b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                  .WithSetting<float>(p => p.WithLabel("Value offset").WithStringFormat("{}{0}%"))
                  .WithCallback((_, axis, offset) =>
                  {
                      var motionProvider = GetMotionProvider(axis, name);
                      if (motionProvider == null)
                          return;

                      motionProvider.Maximum = MathUtils.Clamp(motionProvider.Maximum + offset, 0, 100);
                  }));

            s.RegisterAction($"MotionProvider::{name}::Maximum::Set",
                b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                      .WithSetting<float>(p => p.WithLabel("Value").WithStringFormat("{}{0}%"))
                      .WithCallback((_, axis, value) =>
                      {
                          var motionProvider = GetMotionProvider(axis, name);
                          if (motionProvider == null)
                              return;

                          motionProvider.Maximum = MathUtils.Clamp(value, 0, 100);
                      }));

            s.RegisterAction($"MotionProvider::{name}::Maximum::Drive",
                b => b.WithSetting<DeviceAxis>(p => p.WithLabel("Target axis").WithItemsSource(DeviceAxis.All))
                      .WithCallback((gesture, axis) =>
                      {
                          if (gesture is not IAxisInputGesture axisGesture)
                              return;

                          var motionProvider = GetMotionProvider(axis, name);
                          if (motionProvider == null)
                              return;

                          motionProvider.Maximum = MathUtils.Clamp(motionProvider.Maximum + axisGesture.Delta, 0, 100);
                      }), ShortcutActionDescriptorFlags.AcceptsAxisGesture);
            #endregion
        }
    }
}