using MultiFunPlayer.Common;
using MultiFunPlayer.Input;
using Newtonsoft.Json.Linq;
using Stylet;

namespace MultiFunPlayer.UI.Controls.ViewModels;

internal sealed class InputSettingsViewModel : Conductor<IInputProcessorSettings>.Collection.OneActive, IHandle<SettingsMessage>
{
    public InputSettingsViewModel(IEventAggregator eventAggregator, IEnumerable<IInputProcessorSettings> processorSettings)
    {
        DisplayName = "Input";
        Items.AddRange(processorSettings.OrderBy(p => p.Name));
        ActiveItem = Items.FirstOrDefault();

        eventAggregator.Subscribe(this);
    }

    public void Handle(SettingsMessage message)
    {
        if (message.Action == SettingsAction.Saving)
        {
            if (!message.Settings.EnsureContainsObjects("Input")
             || !message.Settings.TryGetObject(out var settings, "Input"))
                return;

            foreach(var item in Items)
                settings[item.Name] = JObject.FromObject(item);
        }
        else if (message.Action == SettingsAction.Loading)
        {
            if (!message.Settings.TryGetObject(out var settings, "Input"))
                return;

            foreach (var item in Items)
                if (settings.TryGetObject(out var itemSettings, item.Name))
                    itemSettings.Populate(item);
        }
    }
}
