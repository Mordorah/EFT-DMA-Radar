/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using LoneEftDmaRadar.Tarkov.GameWorld.Player.Helpers;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LoneEftDmaRadar.UI.Radar.ViewModels
{
    public sealed class PlayersViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ObservableCollection<WatchlistDisplayEntry> WatchlistEntries { get; } = new();
        public ObservableCollection<HistoryDisplayEntry> HistoryEntries { get; } = new();

        private string _watchlistAccountId = "";
        public string WatchlistAccountId
        {
            get => _watchlistAccountId;
            set { _watchlistAccountId = value; OnPropertyChanged(); }
        }

        private string _watchlistPlayerName = "";
        public string WatchlistPlayerName
        {
            get => _watchlistPlayerName;
            set { _watchlistPlayerName = value; OnPropertyChanged(); }
        }

        private string _watchlistReason = "";
        public string WatchlistReason
        {
            get => _watchlistReason;
            set { _watchlistReason = value; OnPropertyChanged(); }
        }

        private WatchlistTag _watchlistTag = WatchlistTag.Suspicious;
        public WatchlistTag WatchlistTag
        {
            get => _watchlistTag;
            set { _watchlistTag = value; OnPropertyChanged(); }
        }

        public Array WatchlistTags => Enum.GetValues(typeof(WatchlistTag));

        public bool KillFeedEnabled
        {
            get => App.Config.KillFeed.Enabled;
            set
            {
                App.Config.KillFeed.Enabled = value;
                OnPropertyChanged();
            }
        }

        public int KillFeedMaxAge
        {
            get => App.Config.KillFeed.MaxAgeSeconds;
            set
            {
                App.Config.KillFeed.MaxAgeSeconds = value;
                OnPropertyChanged();
            }
        }

        public PlayersViewModel()
        {
            RefreshWatchlist();
            RefreshHistory();
        }

        public void RefreshWatchlist()
        {
            WatchlistEntries.Clear();
            foreach (var entry in App.Watchlist.Entries)
            {
                WatchlistEntries.Add(new WatchlistDisplayEntry
                {
                    AccountId = entry.AccountId,
                    PlayerName = entry.PlayerName,
                    Tag = entry.Tag.ToString(),
                    Reason = entry.Reason,
                    Added = entry.AddedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                });
            }
        }

        public void RefreshHistory()
        {
            HistoryEntries.Clear();
            foreach (var entry in App.PlayerHistory.Entries)
            {
                HistoryEntries.Add(new HistoryDisplayEntry
                {
                    AccountId = entry.AccountId,
                    PlayerName = entry.LastPlayerName,
                    Type = entry.LastPlayerType.ToString(),
                    Side = entry.LastPlayerSide.ToString(),
                    Raids = entry.RaidCount,
                    LastSeen = entry.LastSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                });
            }
        }

        public void AddToWatchlist()
        {
            if (string.IsNullOrWhiteSpace(WatchlistAccountId))
                return;

            App.Watchlist.AddOrUpdate(new WatchlistEntry
            {
                AccountId = WatchlistAccountId.Trim(),
                PlayerName = WatchlistPlayerName.Trim(),
                Reason = WatchlistReason.Trim(),
                Tag = WatchlistTag
            });

            WatchlistAccountId = "";
            WatchlistPlayerName = "";
            WatchlistReason = "";
            RefreshWatchlist();
        }

        public void RemoveFromWatchlist(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId))
                return;

            App.Watchlist.Remove(accountId);
            RefreshWatchlist();
        }

        public void AddHistoryToWatchlist(HistoryDisplayEntry entry)
        {
            if (entry == null)
                return;

            WatchlistAccountId = entry.AccountId;
            WatchlistPlayerName = entry.PlayerName;
            WatchlistReason = "Added from history";
        }

        public void ClearHistory()
        {
            App.PlayerHistory.Clear();
            RefreshHistory();
        }
    }

    public sealed class WatchlistDisplayEntry
    {
        public string AccountId { get; init; }
        public string PlayerName { get; init; }
        public string Tag { get; init; }
        public string Reason { get; init; }
        public string Added { get; init; }
    }

    public sealed class HistoryDisplayEntry
    {
        public string AccountId { get; init; }
        public string PlayerName { get; init; }
        public string Type { get; init; }
        public string Side { get; init; }
        public int Raids { get; init; }
        public string LastSeen { get; init; }
    }
}
