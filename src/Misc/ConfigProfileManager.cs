using System.IO;
using System.Text.Json;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.Misc
{
    /// <summary>
    /// Manages named configuration profiles.
    /// Profiles are saved as separate JSON files in the profiles/ subdirectory.
    /// </summary>
    public static class ConfigProfileManager
    {
        private const string ProfilesDir = "profiles";
        private const string LastProfileFile = "lastProfile.txt";

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true
        };

        private static DirectoryInfo ProfilesPath
        {
            get
            {
                var dir = new DirectoryInfo(Path.Combine(App.ConfigPath.FullName, ProfilesDir));
                if (!dir.Exists)
                    dir.Create();
                return dir;
            }
        }

        /// <summary>
        /// Current active profile name (null = default).
        /// </summary>
        public static string ActiveProfile { get; private set; }

        /// <summary>
        /// Initialize — load the last used profile name.
        /// </summary>
        public static void Initialize()
        {
            try
            {
                var file = Path.Combine(App.ConfigPath.FullName, LastProfileFile);
                if (File.Exists(file))
                    ActiveProfile = File.ReadAllText(file).Trim();
            }
            catch { }
        }

        /// <summary>
        /// Get list of available profile names.
        /// </summary>
        public static List<string> GetAvailableProfiles()
        {
            var profiles = new List<string>();
            try
            {
                var dir = ProfilesPath;
                foreach (var file in dir.GetFiles("*.json"))
                {
                    profiles.Add(Path.GetFileNameWithoutExtension(file.Name));
                }
                profiles.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[ConfigProfiles] Error listing profiles: {ex.Message}");
            }
            return profiles;
        }

        /// <summary>
        /// Save current config as a named profile.
        /// </summary>
        public static void SaveProfile(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            try
            {
                var json = JsonSerializer.Serialize(App.Config, _jsonOptions);
                var path = GetProfilePath(name);
                File.WriteAllText(path, json);
                ActiveProfile = name;
                SaveLastProfile(name);
                DebugLogger.LogDebug($"[ConfigProfiles] Saved profile: {name}");
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[ConfigProfiles] Save error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Load a named profile and apply it to the current config.
        /// DMA settings are preserved to avoid hardware issues.
        /// </summary>
        public static void LoadProfile(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            try
            {
                var path = GetProfilePath(name);
                if (!File.Exists(path))
                    throw new FileNotFoundException($"Profile '{name}' not found.");

                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<EftDmaConfig>(json, _jsonOptions);
                if (loaded is null)
                    throw new InvalidOperationException("Failed to deserialize profile.");

                ApplyProfile(loaded);
                ActiveProfile = name;
                SaveLastProfile(name);
                App.Config.Save();
                DebugLogger.LogDebug($"[ConfigProfiles] Loaded profile: {name}");
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[ConfigProfiles] Load error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Delete a named profile.
        /// </summary>
        public static bool DeleteProfile(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            try
            {
                var path = GetProfilePath(name);
                if (!File.Exists(path))
                    return false;

                File.Delete(path);
                if (string.Equals(ActiveProfile, name, StringComparison.OrdinalIgnoreCase))
                    ActiveProfile = null;

                DebugLogger.LogDebug($"[ConfigProfiles] Deleted profile: {name}");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[ConfigProfiles] Delete error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Export a profile to an arbitrary file path.
        /// </summary>
        public static void ExportProfile(string filePath)
        {
            var json = JsonSerializer.Serialize(App.Config, _jsonOptions);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Import a profile from an arbitrary file path.
        /// </summary>
        public static void ImportProfile(string name, string filePath)
        {
            var json = File.ReadAllText(filePath);
            // Validate it's a valid config
            var test = JsonSerializer.Deserialize<EftDmaConfig>(json, _jsonOptions);
            if (test is null)
                throw new InvalidOperationException("Invalid config file.");

            // Save to profiles directory
            var destPath = GetProfilePath(name);
            File.WriteAllText(destPath, json);
            DebugLogger.LogDebug($"[ConfigProfiles] Imported profile: {name}");
        }

        private static string GetProfilePath(string name)
        {
            // Sanitize name
            var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(ProfilesPath.FullName, safeName + ".json");
        }

        private static void SaveLastProfile(string name)
        {
            try
            {
                File.WriteAllText(Path.Combine(App.ConfigPath.FullName, LastProfileFile), name);
            }
            catch { }
        }

        /// <summary>
        /// Apply a deserialized config to the active App.Config instance.
        /// Preserves DMA hardware settings to prevent connection issues.
        /// Uses JSON round-trip per section for reliable deep copy.
        /// </summary>
        private static void ApplyProfile(EftDmaConfig source)
        {
            var target = App.Config;

            // Preserve DMA settings — these are hardware-specific
            // Copy everything else section-by-section using JSON round-trip
            target.UI = Clone(source.UI);
            target.Loot = Clone(source.Loot);
            target.Containers = Clone(source.Containers);
            target.AimviewWidget = Clone(source.AimviewWidget);
            target.InfoWidget = Clone(source.InfoWidget);
            target.KillFeed = Clone(source.KillFeed);
            target.Device = Clone(source.Device);
            target.MemWrites = Clone(source.MemWrites);
            target.Debug = Clone(source.Debug);
            target.QuestHelper = Clone(source.QuestHelper);
            target.Hideout = Clone(source.Hideout);
            target.LootFilters = Clone(source.LootFilters);
            target.Misc = Clone(source.Misc);
            target.Visibility = Clone(source.Visibility);
            target.WebRadar = Clone(source.WebRadar);
            // DMA, Hotkeys, RadarColors, PanelLayout, Cache — preserved
        }

        private static T Clone<T>(T source)
        {
            var json = JsonSerializer.Serialize(source, _jsonOptions);
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
    }
}
