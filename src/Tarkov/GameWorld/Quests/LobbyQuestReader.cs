/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using Collections.Pooled;
using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.IL2CPP;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.UI.Misc;
using SDK;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Quests
{
    /// <summary>
    /// Reads active quest IDs and progress from the player profile while in the lobby (main menu).
    /// Uses IL2CPP metadata to locate TarkovApplication singleton.
    /// </summary>
    internal static class LobbyQuestReader
    {
        private static ulong _cachedTarkovApp;
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
        private static DateTime _lastPoll = DateTime.MinValue;
        private static QuestMemoryReader _memoryReader;

        /// <summary>
        /// Set of quest IDs that are currently started (status=2) in the lobby profile.
        /// Null if unavailable.
        /// </summary>
        public static IReadOnlySet<string> StartedQuestIds { get; private set; }

        /// <summary>
        /// Per-quest completed condition IDs read from the lobby profile.
        /// Key: quest ID, Value: set of completed condition IDs.
        /// </summary>
        public static IReadOnlyDictionary<string, HashSet<string>> CompletedConditions { get; private set; }

        /// <summary>
        /// Condition counters read from the lobby profile (same format as QuestMemoryReader).
        /// Key: condition ID, Value: (CurrentCount, TargetCount).
        /// </summary>
        public static IReadOnlyDictionary<string, (int CurrentCount, int TargetCount)> ConditionCounters { get; private set; }

        /// <summary>
        /// Try to read lobby quests. Call periodically from the UI refresh timer.
        /// Returns true if quest data was successfully read.
        /// </summary>
        public static bool TryRefresh()
        {
            if (DateTime.UtcNow - _lastPoll < PollInterval)
                return StartedQuestIds != null;

            _lastPoll = DateTime.UtcNow;

            try
            {
                if (Memory == null || !Memory.Ready)
                    return false;

                // Don't read in lobby if we're in raid — QuestManager handles that
                if (Memory.InRaid)
                {
                    StartedQuestIds = null;
                    CompletedConditions = null;
                    ConditionCounters = null;
                    return false;
                }

                var profile = GetLobbyProfile();
                if (profile == 0)
                    return false;

                var questData = ReadStartedQuests(profile);
                if (questData != null)
                {
                    StartedQuestIds = questData.Value.QuestIds;
                    CompletedConditions = questData.Value.CompletedConditions;

                    // Read condition counters from profile
                    _memoryReader ??= new QuestMemoryReader(profile);
                    try
                    {
                        ConditionCounters = _memoryReader.ReadConditionCounters();
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.LogDebug($"[LobbyQuestReader] Error reading counters: {ex.Message}");
                        ConditionCounters = null;
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[LobbyQuestReader] Error: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Clear cached data (call when entering raid or disconnecting).
        /// </summary>
        public static void Reset()
        {
            StartedQuestIds = null;
            CompletedConditions = null;
            ConditionCounters = null;
            _cachedTarkovApp = 0;
            _memoryReader = null;
        }

        #region Profile Access

        private static ulong GetLobbyProfile()
        {
            try
            {
                var appInstance = GetTarkovApplicationInstance();
                if (!MemDMA.IsValidVirtualAddress(appInstance))
                    return 0;

                var menuOperation = Memory.ReadValue<ulong>(appInstance + Offsets.TarkovApplication._menuOperation);
                if (!MemDMA.IsValidVirtualAddress(menuOperation))
                    return 0;

                var profile = Memory.ReadValue<ulong>(menuOperation + Offsets.MainMenuShowOperation._profile);
                if (!MemDMA.IsValidVirtualAddress(profile))
                    return 0;

                return profile;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[LobbyQuestReader] GetLobbyProfile: {ex.Message}");
                return 0;
            }
        }

        private static ulong GetTarkovApplicationInstance()
        {
            // Use cached value if still valid
            if (MemDMA.IsValidVirtualAddress(_cachedTarkovApp))
            {
                try
                {
                    var test = Memory.ReadValue<ulong>(_cachedTarkovApp + Offsets.TarkovApplication._menuOperation);
                    if (MemDMA.IsValidVirtualAddress(test))
                        return _cachedTarkovApp;
                }
                catch { }
                _cachedTarkovApp = 0;
            }

            if (!IL2CPPLib.Initialized)
                return 0;

            try
            {
                var klassPtr = IL2CPPLib.Class.FindClass("EFT.TarkovApplication");
                if (!MemDMA.IsValidVirtualAddress(klassPtr))
                    return 0;

                // Scan static_fields at multiple offsets — instance is typically at +0x30
                ulong instance = 0;
                ulong currentKlassPtr = klassPtr;

                for (int depth = 0; depth < 10 && instance == 0; depth++)
                {
                    var currentKlass = Memory.ReadValue<IL2CPPLib.Class>(currentKlassPtr);

                    if (MemDMA.IsValidVirtualAddress(currentKlass.static_fields))
                    {
                        for (uint sfOff = 0; sfOff <= 0x38; sfOff += 8)
                        {
                            try
                            {
                                var candidate = Memory.ReadValue<ulong>(currentKlass.static_fields + sfOff);
                                if (!MemDMA.IsValidVirtualAddress(candidate))
                                    continue;

                                var testMenuOp = Memory.ReadValue<ulong>(candidate + Offsets.TarkovApplication._menuOperation);
                                if (MemDMA.IsValidVirtualAddress(testMenuOp))
                                {
                                    instance = candidate;
                                    break;
                                }
                            }
                            catch { }
                        }
                    }

                    if (!MemDMA.IsValidVirtualAddress(currentKlass.parent))
                        break;
                    currentKlassPtr = currentKlass.parent;
                }

                if (MemDMA.IsValidVirtualAddress(instance))
                {
                    _cachedTarkovApp = instance;
                    DebugLogger.LogDebug($"[LobbyQuestReader] Found TarkovApplication @ 0x{instance:X}");
                }

                return instance;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[LobbyQuestReader] IL2CPP lookup: {ex.Message}");
                return 0;
            }
        }

        #endregion

        #region Quest Reading

        private readonly record struct LobbyQuestData(
            HashSet<string> QuestIds,
            Dictionary<string, HashSet<string>> CompletedConditions);

        private static LobbyQuestData? ReadStartedQuests(ulong profile)
        {
            try
            {
                var questsDataPtr = Memory.ReadPtr(profile + Offsets.Profile.QuestsData);
                if (questsDataPtr == 0)
                    return null;

                using var questsList = UnityList<ulong>.Create(questsDataPtr, false);
                var questIds = new HashSet<string>(StringComparer.Ordinal);
                var completedConditions = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                var memReader = new QuestMemoryReader(profile);

                foreach (var qDataEntry in questsList)
                {
                    try
                    {
                        var qStatus = Memory.ReadValue<int>(qDataEntry + Offsets.QuestStatusData.Status);
                        if (qStatus != 2) // Only Started quests
                            continue;

                        var qIdPtr = Memory.ReadPtr(qDataEntry + Offsets.QuestStatusData.Id);
                        var qId = Memory.ReadUnicodeString(qIdPtr, 64, false);
                        if (string.IsNullOrEmpty(qId))
                            continue;

                        questIds.Add(qId);

                        // Read completed conditions for this quest
                        try
                        {
                            var completedPtr = Memory.ReadPtr(qDataEntry + Offsets.QuestStatusData.CompletedConditions);
                            if (completedPtr != 0)
                            {
                                using var conditions = new PooledList<string>();
                                memReader.ReadCompletedConditionsHashSet(completedPtr, conditions);
                                if (conditions.Count > 0)
                                    completedConditions[qId] = new HashSet<string>(conditions, StringComparer.OrdinalIgnoreCase);
                            }
                        }
                        catch { }
                    }
                    catch { }
                }

                return questIds.Count > 0
                    ? new LobbyQuestData(questIds, completedConditions)
                    : null;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[LobbyQuestReader] ReadStartedQuests: {ex.Message}");
                return null;
            }
        }

        #endregion
    }
}
