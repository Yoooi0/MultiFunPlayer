using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.OutputTarget;
using Newtonsoft.Json.Linq;
using Stylet;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;

namespace MultiFunPlayer.ViewModels
{
    public class OutputTargetViewModel : Conductor<IOutputTarget>.Collection.AllActive, IHandle<AppSettingsMessage>, IDisposable
    {
        public IOutputTarget SelectedItem { get; set; }

        public OutputTargetViewModel(IEventAggregator eventAggregator, IEnumerable<IOutputTarget> targets)
        {
            eventAggregator.Subscribe(this);
            foreach (var target in targets)
                Items.Add(target);
        }

        protected override void OnActivate()
        {
            ActivateAndSetParent(Items);
            base.OnActivate();
        }

        public void Handle(AppSettingsMessage message)
        {
            if (message.Type == AppSettingsMessageType.Saving)
            {
                if (!message.Settings.EnsureContainsObjects("OutputTarget")
                 || !message.Settings.TryGetObject(out var settings, "OutputTarget"))
                    return;

                settings[nameof(SelectedItem)] = SelectedItem.Name;
            }
            else if (message.Type == AppSettingsMessageType.Loading)
            {
                if (!message.Settings.TryGetObject(out var settings, "OutputTarget"))
                    return;

                if (settings.TryGetValue(nameof(SelectedItem), out var selectedItemToken))
                    SelectedItem = Items.FirstOrDefault(x => string.Equals(x.Name, selectedItemToken.ToObject<string>())) ?? Items.First();
            }
        }

        protected virtual void Dispose(bool disposing) { }

        void IDisposable.Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
