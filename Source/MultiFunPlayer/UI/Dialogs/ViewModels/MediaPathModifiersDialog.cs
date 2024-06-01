using MultiFunPlayer.Common;
using MultiFunPlayer.MediaSource.MediaResource.Modifier;
using System.ComponentModel;
using System.Reflection;
using System.Windows;

namespace MultiFunPlayer.UI.Dialogs.ViewModels;

internal sealed class MediaPathModifiersDialog(ObservableConcurrentCollection<IMediaPathModifier> mediaPathModifiers)
{
    public ObservableConcurrentCollection<IMediaPathModifier> MediaPathModifiers { get; } = mediaPathModifiers;
    public Dictionary<string, Type> MediaPathModifierTypes { get; } = ReflectionUtils.FindImplementations<IMediaPathModifier>()
                                                                                     .ToDictionary(t => t.GetCustomAttribute<DisplayNameAttribute>(inherit: false).DisplayName, t => t);

    public void OnAdd(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not KeyValuePair<string, Type> pair)
            return;

        var (_, type) = pair;
        var modifier = (IMediaPathModifier)Activator.CreateInstance(type);
        MediaPathModifiers.Add(modifier);
    }

    public void OnMoveUp(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IMediaPathModifier modifier)
            return;

        var index = MediaPathModifiers.IndexOf(modifier);
        if (index < 1)
            return;

        MediaPathModifiers.Move(index, index - 1);
    }

    public void OnMoveDown(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IMediaPathModifier modifier)
            return;

        var index = MediaPathModifiers.IndexOf(modifier);
        if (index == -1 || index == MediaPathModifiers.Count - 1)
            return;

        MediaPathModifiers.Move(index, index + 1);
    }

    public void OnRemove(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not IMediaPathModifier modifier)
            return;

        MediaPathModifiers.Remove(modifier);
    }
}