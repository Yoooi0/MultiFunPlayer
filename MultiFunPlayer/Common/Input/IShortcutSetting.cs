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
    public interface IShortcutSetting
    {
        string Description { get; }
        object Value { get; set; }
    }

    public interface IShortcutSetting<T> : IShortcutSetting
    {
        object IShortcutSetting.Value
        {
            get => Value;
            set => Value = (T)value;
        }

        new T Value { get; set; }
    }

    [AddINotifyPropertyChangedInterface]
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class ShortcutSetting<T> : IShortcutSetting<T>
    {
        [JsonProperty] public T Value { get; set; }
        public string Description { get; }

        public ShortcutSetting(string description) => Description = description;
    }
}
