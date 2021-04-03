using MultiFunPlayer.Common;
using MultiFunPlayer.Common.Messages;
using MultiFunPlayer.OutputTarget;
using Stylet;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MultiFunPlayer.ViewModels
{
    public class OutputTargetViewModel : Conductor<IOutputTarget>.Collection.OneActive, IHandle<AppSettingsMessage>, IDisposable
    {
        public OutputTargetViewModel(IEventAggregator eventAggregator, IEnumerable<IOutputTarget> targets)
        {
            eventAggregator.Subscribe(this);
            Items.AddRange(targets);
        }

        public void Handle(AppSettingsMessage message)
        {
            if (message.Type == AppSettingsMessageType.Saving)
            {
                if (!message.Settings.EnsureContainsObjects("OutputTarget")
                 || !message.Settings.TryGetObject(out var settings, "OutputTarget"))
                    return;

                if(ActiveItem != null)
                    settings[nameof(ActiveItem)] = ActiveItem.Name;
            }
            else if (message.Type == AppSettingsMessageType.Loading)
            {
                if (!message.Settings.TryGetObject(out var settings, "OutputTarget"))
                    return;

                if (settings.TryGetValue(nameof(ActiveItem), out var selectedItemToken))
                    ChangeActiveItem(Items.FirstOrDefault(x => string.Equals(x.Name, selectedItemToken.ToObject<string>())) ?? Items[0], closePrevious: false);
            }
        }

        protected override void ChangeActiveItem(IOutputTarget newItem, bool closePrevious)
        {
            if (ActiveItem != null && newItem != null)
            {
                newItem.ContentVisible = ActiveItem.ContentVisible;
                ActiveItem.ContentVisible = false;
            }

            base.ChangeActiveItem(newItem, closePrevious);
        }

        protected virtual void Dispose(bool disposing) { }

        void IDisposable.Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
