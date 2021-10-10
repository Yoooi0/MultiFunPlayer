using Newtonsoft.Json;
using PropertyChanged;
using Stylet;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MultiFunPlayer.Common.Input
{
    public interface IShortcutAction
    {
        IShortcutActionDescriptor Descriptor { get; }
        IReadOnlyObservableCollection<IShortcutSetting> Settings { get; }
        void Invoke(IInputGesture gesture);
    }

    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class ShortcutAction : IShortcutAction, INotifyPropertyChanged
    {
        private readonly Delegate _action;

        [JsonProperty] public IShortcutActionDescriptor Descriptor { get; }
        [JsonProperty(ItemTypeNameHandling = TypeNameHandling.Objects)] public IReadOnlyObservableCollection<IShortcutSetting> Settings { get; }

        public string DisplayName => Settings.Count == 0 ? $"{Descriptor.Name}" : $"{Descriptor.Name} [{string.Join(", ", Settings.Select(s => s.Value?.ToString() ?? "null"))}]";

        [JsonConstructor]
        public ShortcutAction(IShortcutActionDescriptor descriptor, Delegate action, [JsonProperty(ItemTypeNameHandling = TypeNameHandling.Objects)] IEnumerable<IShortcutSetting> settings)
        {
            _action = action;
            Descriptor = descriptor;
            Settings = new BindableCollection<IShortcutSetting>(settings);
        }

        public ShortcutAction(IShortcutActionDescriptor descriptor, Delegate action, IEnumerable<(Type Type, string Description)> settings)
        {
            _action = action;
            Descriptor = descriptor;
            Settings = new BindableCollection<IShortcutSetting>(settings.Select(s =>
            {
                var type = typeof(ShortcutSetting<>).MakeGenericType(s.Type);
                var setting = (IShortcutSetting)Activator.CreateInstance(type, s.Description);

                if (setting is INotifyPropertyChanged o)
                    o.PropertyChanged += OnSettingsValueChanged;

                return setting;
            }));
        }

        [SuppressPropertyChangedWarnings]
        private void OnSettingsValueChanged(object sender, PropertyChangedEventArgs e)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));

        public void Invoke(IInputGesture gesture) => _action?.DynamicInvoke(Settings.Select(s => s.Value).Prepend(gesture).ToArray());

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
