using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using Newtonsoft.Json.Linq;
using Stylet;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MultiFunPlayer.OutputTarget
{
    public abstract class AbstractOutputTarget : Screen, IHandle<AppSettingsMessage>, IDisposable, IOutputTarget
    {
        public abstract string Name { get; }
        public abstract OutputTargetStatus Status { get; protected set; }

        public ObservableConcurrentDictionary<DeviceAxis, DeviceAxisSettings> AxisSettings { get; protected set; }
        public int UpdateRate { get; set; }
        protected IDeviceAxisValueProvider ValueProvider { get; }

        protected AbstractOutputTarget(IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
        {
            eventAggregator.Subscribe(this);
            ValueProvider = valueProvider;

            AxisSettings = new ObservableConcurrentDictionary<DeviceAxis, DeviceAxisSettings>(EnumUtils.GetValues<DeviceAxis>().ToDictionary(a => a, _ => new DeviceAxisSettings()));
            UpdateRate = 60;
        }

        protected abstract void HandleSettings(JObject settings, AppSettingsMessageType type);
        public void Handle(AppSettingsMessage message)
        {
            if (message.Type == AppSettingsMessageType.Saving)
            {
                if (!message.Settings.ContainsKey(Name))
                    message.Settings[Name] = new JObject();

                var settings = message.Settings[Name] as JObject;
                settings[nameof(UpdateRate)] = new JValue(UpdateRate);
                settings[nameof(AxisSettings)] = JObject.FromObject(AxisSettings);

                HandleSettings(settings, message.Type);
            }
            else if (message.Type == AppSettingsMessageType.Loading)
            {
                if (!message.Settings.ContainsKey(Name))
                    return;

                var settings = message.Settings[Name] as JObject;
                if (settings.TryGetValue(nameof(UpdateRate), out var updateRateToken))
                    UpdateRate = updateRateToken.ToObject<int>();
                if (settings.TryGetValue(nameof(AxisSettings), out var axisSettingsToken))
                    foreach (var (axis, axisSettings) in axisSettingsToken.ToObject<Dictionary<DeviceAxis, DeviceAxisSettings>>())
                        AxisSettings[axis] = axisSettings;

                HandleSettings(settings, message.Type);
            }
        }

        protected virtual void Dispose(bool disposing) { }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}