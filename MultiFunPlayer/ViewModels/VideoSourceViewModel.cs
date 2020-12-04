using MultiFunPlayer.VideoSource;
using Stylet;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace MultiFunPlayer.ViewModels
{
    public class VideoSourceViewModel : PropertyChangedBase
    {
        public List<IVideoSource> Sources { get; }

        public VideoSourceViewModel(IEnumerable<IVideoSource> sources)
        {
            Sources = sources.ToList();
        }

        public void OnSourceClick(object sender, RoutedEventArgs e)
        {
            Sources.ForEach(p => p.Stop());
            if (sender is FrameworkElement element && element.DataContext is IVideoSource source)
                source.Start();
        }
    }
}
