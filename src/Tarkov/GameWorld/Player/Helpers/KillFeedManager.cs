namespace LoneEftDmaRadar.Tarkov.GameWorld.Player.Helpers
{
    /// <summary>
    /// Tracks recent player deaths for kill feed display.
    /// Thread-safe, static, max 10 entries.
    /// </summary>
    public static class KillFeedManager
    {
        private const int MaxEntries = 10;
        private static readonly Lock _sync = new();
        private static readonly List<KillFeedEntry> _entries = new(MaxEntries);

        /// <summary>
        /// Thread-safe snapshot of current kill feed entries (newest first).
        /// </summary>
        public static IReadOnlyList<KillFeedEntry> Entries
        {
            get
            {
                lock (_sync)
                    return _entries.ToList();
            }
        }

        /// <summary>
        /// Push a new kill event to the feed.
        /// </summary>
        public static void Push(string victimName, PlayerType victimType, SDK.Enums.EPlayerSide victimSide)
        {
            lock (_sync)
            {
                _entries.Insert(0, new KillFeedEntry
                {
                    VictimName = victimName,
                    VictimType = victimType,
                    VictimSide = victimSide,
                    TimestampUtc = DateTime.UtcNow
                });
                while (_entries.Count > MaxEntries)
                    _entries.RemoveAt(_entries.Count - 1);
            }
        }

        /// <summary>
        /// Clear all kill feed entries (called on new raid).
        /// </summary>
        public static void Reset()
        {
            lock (_sync)
                _entries.Clear();
        }

        /// <summary>
        /// Remove entries older than the specified duration.
        /// </summary>
        public static void PruneOlderThan(TimeSpan maxAge)
        {
            var cutoff = DateTime.UtcNow - maxAge;
            lock (_sync)
                _entries.RemoveAll(e => e.TimestampUtc < cutoff);
        }
    }

    /// <summary>
    /// A single kill feed entry.
    /// </summary>
    public sealed class KillFeedEntry
    {
        public string VictimName { get; init; }
        public PlayerType VictimType { get; init; }
        public SDK.Enums.EPlayerSide VictimSide { get; init; }
        public DateTime TimestampUtc { get; init; }

        /// <summary>
        /// Seconds since this kill event occurred.
        /// </summary>
        public double AgeSeconds => (DateTime.UtcNow - TimestampUtc).TotalSeconds;
    }
}
