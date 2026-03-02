using LoneEftDmaRadar.UI.Radar.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace LoneEftDmaRadar.UI.Radar.Views
{
    public partial class PlayersTab : UserControl
    {
        public PlayersViewModel ViewModel { get; }

        public PlayersTab()
        {
            InitializeComponent();
            DataContext = ViewModel = new PlayersViewModel();
        }

        private void AddWatchlist_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.AddToWatchlist();
        }

        private void RemoveWatchlist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string accountId)
                ViewModel.RemoveFromWatchlist(accountId);
        }

        private void RefreshHistory_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RefreshHistory();
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ClearHistory();
        }

        private void WatchFromHistory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is HistoryDisplayEntry entry)
                ViewModel.AddHistoryToWatchlist(entry);
        }
    }
}
