using MultiFunPlayer.Player;
using Stylet;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace MultiFunPlayer.ViewModels
{
    public class PlayerViewModel : PropertyChangedBase
    {
        public List<IVideoPlayer> Players { get; }

        public PlayerViewModel(IEnumerable<IVideoPlayer> players)
        {
            Players = players.ToList();
        }

        public void OnPlayerClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is IVideoPlayer player)
                player.Start();
        }
    }
}
