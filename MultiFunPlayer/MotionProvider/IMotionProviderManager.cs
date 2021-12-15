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

public interface IMotionProviderManager
{
    IEnumerable<string> MotionProviderNames { get; }

    IMotionProvider GetMotionProvider(DeviceAxis axis, string motionProviderName);
    float? Update(DeviceAxis axis, string motionProviderName, float deltaTime);
    void RegisterShortcuts(IShortcutManager shortcutManager);
}

public class MotionProviderManager : IMotionProviderManager, IHandle<AppSettingsMessage>
{
    protected Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly IEventAggregator _eventAggregator;
    private readonly HashSet<string> _motionProviderNames;
    private readonly Dictionary<DeviceAxis, Dictionary<string, IMotionProvider>> _motionProviders;

    public IEnumerable<string> MotionProviderNames => _motionProviderNames;

    public MotionProviderManager(IEventAggregator eventAggregator, Func<IEnumerable<IMotionProvider>> motionProviderFactory)
    {
        _eventAggregator = eventAggregator;
        _eventAggregator.Subscribe(this);

        var motionProviderTypes = ReflectionUtils.FindImplementations<IMotionProvider>();
        _motionProviderNames = motionProviderTypes.Select(t => t.GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName)
                                                  .ToHashSet();
        _motionProviders = DeviceAxis.All.ToDictionary(a => a, a => motionProviderFactory().ToDictionary(p => p.GetType().GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName, 
                                                                                                         p => p));

        foreach(var (_, motionProviders) in _motionProviders)
            foreach(var (_, motionProvider) in motionProviders)
                motionProvider.SyncRequest += OnMotionProviderSyncRequest;
    }

    private void OnMotionProviderSyncRequest(object sender, EventArgs e)
    {
        if (sender is not IMotionProvider motionProvider)
            return;

        var axis = GetDeviceAxis(motionProvider);
        if(axis == null)
        {
            Logger.Warn("Could not find motion provider for axis! [Axis: {0}, Name: {1}]", axis, motionProvider.Name);
            return;
        }

        _eventAggregator.Publish(new SyncRequestMessage(axis));
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

    private DeviceAxis GetDeviceAxis(IMotionProvider provider)
    {
        foreach (var (axis, motionProviders) in _motionProviders)
            foreach (var (_, motionProvider) in motionProviders)
                if (motionProvider == provider)
                    return axis;
        
        return null;
    }

    public float? Update(DeviceAxis axis, string motionProviderName, float deltaTime)
    {
        var motionProvider = GetMotionProvider(axis, motionProviderName);
        motionProvider?.Update(deltaTime);
        return motionProvider?.Value;
    }

    public void Handle(AppSettingsMessage message)
    {
        if (message.Type == AppSettingsMessageType.Saving)
        {
            message.Settings["MotionProvider"] = JObject.FromObject(_motionProviders.ToDictionary(x => x.Key, x => x.Value.Values.Select(p => new TypedValue(p)).ToList()));
        }
        else if (message.Type == AppSettingsMessageType.Loading)
        {
            if (message.Settings.TryGetValue<Dictionary<DeviceAxis, List<TypedValue>>>("MotionProvider", out var motionProviderMap))
            {
                foreach (var (axis, motionProviders) in motionProviderMap)
                {
                    foreach (var motionProvider in motionProviders.Select(x => x.Value as IMotionProvider))
                    {
                        var currentMotionProvider = _motionProviders[axis][motionProvider.Name];
                        if (currentMotionProvider != null)
                            currentMotionProvider.SyncRequest -= OnMotionProviderSyncRequest;

                        motionProvider.SyncRequest += OnMotionProviderSyncRequest;
                        _motionProviders[axis][motionProvider.Name] = motionProvider;
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