using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Player.Helpers
{
    /// <summary>
    /// In-memory player encounter history for the current session.
    /// Tracks human players seen across raids. Resets on app restart.
    /// </summary>
    public sealed class PlayerHistory
    {
        private readonly Lock _sync = new();
        private readonly Dictionary<string, PlayerHistoryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loggedThisRaid = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Thread-safe snapshot of all history entries, newest first.
        /// </summary>
        public IReadOnlyList<PlayerHistoryEntry> Entries
        {
            get
            {
                lock (_sync)
                    return _entries.Values
                        .OrderByDescending(e => e.LastSeenUtc)
                        .ToList();
            }
        }

        public int Count
        {
            get
            {
                lock (_sync)
                    return _entries.Count;
            }
        }

        /// <summary>
        /// Log a player encounter. Updates LastSeen if already known.
        /// Only tracks human players (PMC/PScav).
        /// </summary>
        public void AddOrUpdate(AbstractPlayer player)
        {
            if (player is not ObservedPlayer observed || !observed.IsHuman)
                return;

            var accountId = ReadAccountId(observed);
            if (string.IsNullOrWhiteSpace(accountId))
                return;

            lock (_sync)
            {
                if (_loggedThisRaid.Contains(accountId))
                {
                    // Already logged this raid — just update LastSeen
                    if (_entries.TryGetValue(accountId, out var existing))
                        existing.LastSeenUtc = DateTime.UtcNow;
                    return;
                }
                _loggedThisRaid.Add(accountId);

                if (_entries.TryGetValue(accountId, out var entry))
                {
                    entry.LastSeenUtc = DateTime.UtcNow;
                    entry.RaidCount++;
                    entry.LastPlayerName = observed.Name;
                    entry.LastPlayerType = observed.Type;
                    entry.LastPlayerSide = observed.PlayerSide;
                }
                else
                {
                    _entries[accountId] = new PlayerHistoryEntry
                    {
                        AccountId = accountId,
                        LastPlayerName = observed.Name,
                        LastPlayerType = observed.Type,
                        LastPlayerSide = observed.PlayerSide,
                        FirstSeenUtc = DateTime.UtcNow,
                        LastSeenUtc = DateTime.UtcNow,
                        RaidCount = 1
                    };
                }
            }
        }

        /// <summary>
        /// Check if a player has been seen before.
        /// </summary>
        public bool TryGet(string accountId, out PlayerHistoryEntry entry)
        {
            lock (_sync)
                return _entries.TryGetValue(accountId, out entry);
        }

        /// <summary>
        /// Called on new raid — clears per-raid tracking but keeps history.
        /// </summary>
        public void OnNewRaid()
        {
            lock (_sync)
                _loggedThisRaid.Clear();
        }

        /// <summary>
        /// Clear all history.
        /// </summary>
        public void Clear()
        {
            lock (_sync)
            {
                _entries.Clear();
                _loggedThisRaid.Clear();
            }
        }

        private static string ReadAccountId(ObservedPlayer player)
        {
            try
            {
                var ptr = Memory.ReadPtr(player + Offsets.ObservedPlayerView.AccountId);
                if (ptr == 0) return null;
                return Memory.ReadUnicodeString(ptr);
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// A single player history entry.
    /// </summary>
    public sealed class PlayerHistoryEntry
    {
        public string AccountId { get; set; }
        public string LastPlayerName { get; set; }
        public PlayerType LastPlayerType { get; set; }
        public SDK.Enums.EPlayerSide LastPlayerSide { get; set; }
        public DateTime FirstSeenUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public int RaidCount { get; set; }
    }
}
