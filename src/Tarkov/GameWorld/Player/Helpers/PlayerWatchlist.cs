using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Player.Helpers
{
    /// <summary>
    /// Persistent player watchlist. Stores entries keyed by AccountId.
    /// Persisted as JSON at %AppData%/LoneEftDMARadar/watchlist.json.
    /// </summary>
    public sealed class PlayerWatchlist
    {
        private static readonly string _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LoneEftDMARadar", "watchlist.json");

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly Lock _sync = new();
        private readonly Dictionary<string, WatchlistEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Thread-safe snapshot of all watchlist entries.
        /// </summary>
        public IReadOnlyList<WatchlistEntry> Entries
        {
            get
            {
                lock (_sync)
                    return _entries.Values.ToList();
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
        /// Check if an AccountId is on the watchlist.
        /// </summary>
        public bool TryGet(string accountId, out WatchlistEntry entry)
        {
            lock (_sync)
                return _entries.TryGetValue(accountId, out entry);
        }

        /// <summary>
        /// Add or update a watchlist entry.
        /// </summary>
        public void AddOrUpdate(WatchlistEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.AccountId))
                return;
            lock (_sync)
                _entries[entry.AccountId] = entry;
            Save();
        }

        /// <summary>
        /// Remove an entry by AccountId.
        /// </summary>
        public bool Remove(string accountId)
        {
            bool removed;
            lock (_sync)
                removed = _entries.Remove(accountId);
            if (removed)
                Save();
            return removed;
        }

        /// <summary>
        /// Clear all watchlist entries.
        /// </summary>
        public void Clear()
        {
            lock (_sync)
                _entries.Clear();
            Save();
        }

        /// <summary>
        /// Load watchlist from disk.
        /// </summary>
        public void Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return;
                var json = File.ReadAllText(_filePath);
                var entries = JsonSerializer.Deserialize<List<WatchlistEntry>>(json, _jsonOptions);
                if (entries is null)
                    return;
                lock (_sync)
                {
                    _entries.Clear();
                    foreach (var entry in entries)
                    {
                        if (!string.IsNullOrWhiteSpace(entry.AccountId))
                            _entries[entry.AccountId] = entry;
                    }
                }
                DebugLogger.LogDebug($"[Watchlist] Loaded {entries.Count} entries.");
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[Watchlist] Load error: {ex.Message}");
            }
        }

        /// <summary>
        /// Save watchlist to disk.
        /// </summary>
        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir!);
                List<WatchlistEntry> snapshot;
                lock (_sync)
                    snapshot = _entries.Values.ToList();
                var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[Watchlist] Save error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// A single watchlist entry.
    /// </summary>
    public sealed class WatchlistEntry
    {
        [JsonPropertyName("accountId")]
        public string AccountId { get; set; } = string.Empty;

        [JsonPropertyName("playerName")]
        public string PlayerName { get; set; } = string.Empty;

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        [JsonPropertyName("tag")]
        public WatchlistTag Tag { get; set; } = WatchlistTag.Suspicious;

        [JsonPropertyName("addedUtc")]
        public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Classification tag for watchlist entries.
    /// </summary>
    public enum WatchlistTag
    {
        Suspicious,
        Cheater,
        Streamer,
        Friendly,
        KnownPlayer
    }
}
