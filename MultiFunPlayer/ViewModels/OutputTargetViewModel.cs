using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.OutputTarget;
using Newtonsoft.Json.Linq;
using Stylet;
using System;
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
                message.Settings.EnsureContains<JObject>("OutputTarget");
                message.Settings["OutputTarget"][nameof(SelectedItem)] = SelectedItem.Name;
            }
            else if (message.Type == AppSettingsMessageType.Loading)
            {
                if (!message.Settings.ContainsKey("OutputTarget"))
                    return;

                SelectedItem = Items.FirstOrDefault(x => string.Equals(x.Name, message.Settings["OutputTarget"][nameof(SelectedItem)].ToObject<string>())) ?? Items.First();
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
