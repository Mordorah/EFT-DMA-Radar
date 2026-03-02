using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Locks time of day to a configurable hour.
    /// Navigates FPSCamera → MBOIT_Scattering → TOD_Sky → TOD_CycleParameters + TOD_Time.
    /// </summary>
    public sealed class TimeOfDay : MemWriteFeature<TimeOfDay>
    {
        private static readonly HashSet<string> ExcludedMaps = new(StringComparer.OrdinalIgnoreCase)
        {
            "factory4_day",
            "factory4_night",
            "laboratory",
            "Labyrinth"
        };

        private bool _lastEnabledState;
        private ulong _cachedTodTime;
        private ulong _cachedTodCycle;

        public override bool Enabled
        {
            get => App.Config.MemWrites.TimeOfDayEnabled;
            set => App.Config.MemWrites.TimeOfDayEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(250);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                var stateChanged = Enabled != _lastEnabledState;

                if (!Enabled)
                {
                    if (stateChanged)
                    {
                        ResetTime();
                        _lastEnabledState = false;
                    }
                    return;
                }

                // Skip excluded maps (indoor/fixed time maps)
                var mapId = Memory.Game?.MapID;
                if (mapId != null && ExcludedMaps.Contains(mapId))
                    return;

                ApplyTimeOfDay();

                if (stateChanged)
                    _lastEnabledState = true;
            }
            catch
            {
                ClearCache();
            }
        }

        private void ApplyTimeOfDay()
        {
            var (todTime, todCycle) = GetPointers();
            if (todTime == 0 || todCycle == 0)
                return;

            var targetHour = Math.Clamp(App.Config.MemWrites.TimeOfDayHour, 0f, 24f);

            // Lock time
            var currentLock = Memory.ReadValue<bool>(todTime + SDK.Offsets.TOD_Time.LockCurrentTime, false);
            if (!currentLock)
                Memory.WriteValue(todTime + SDK.Offsets.TOD_Time.LockCurrentTime, true);

            // Set hour
            var currentHour = Memory.ReadValue<float>(todCycle + SDK.Offsets.TOD_CycleParameters.Hour, false);
            if (currentHour >= 0f && currentHour <= 24f && Math.Abs(currentHour - targetHour) > 0.01f)
                Memory.WriteValue(todCycle + SDK.Offsets.TOD_CycleParameters.Hour, targetHour);
        }

        private void ResetTime()
        {
            try
            {
                if (MemDMA.IsValidVirtualAddress(_cachedTodTime))
                    Memory.WriteValue(_cachedTodTime + SDK.Offsets.TOD_Time.LockCurrentTime, false);
            }
            catch { }
            ClearCache();
        }

        private (ulong todTime, ulong todCycle) GetPointers()
        {
            if (MemDMA.IsValidVirtualAddress(_cachedTodTime) && MemDMA.IsValidVirtualAddress(_cachedTodCycle))
                return (_cachedTodTime, _cachedTodCycle);

            try
            {
                var fpsCamera = MemDMA.CameraManager?.FPSCamera ?? 0;
                if (!MemDMA.IsValidVirtualAddress(fpsCamera))
                    return (0, 0);

                // Find MBOIT_Scattering component on the FPS camera's GameObject
                var scatteringObjectClass = MonoBehaviour.GetComponentFromBehaviour(fpsCamera, "MBOIT_Scattering");
                if (!MemDMA.IsValidVirtualAddress(scatteringObjectClass))
                    return (0, 0);

                var scatteringObj = Memory.ReadPtr(scatteringObjectClass + ObjectClass.MonoBehaviourOffset);
                if (!MemDMA.IsValidVirtualAddress(scatteringObj))
                    return (0, 0);

                // TOD_Scattering → Sky → Cycle / TOD_Components → TOD_Time
                var todSky = Memory.ReadPtr(scatteringObj + SDK.Offsets.TOD_Scattering.Sky);
                if (!MemDMA.IsValidVirtualAddress(todSky))
                    return (0, 0);

                var todCycle = Memory.ReadPtr(todSky + SDK.Offsets.TOD_Sky.Cycle);
                if (!MemDMA.IsValidVirtualAddress(todCycle))
                    return (0, 0);

                // Validate by reading class name
                var cycleName = ObjectClass.ReadName(todCycle, 64, false);
                if (cycleName != "TOD_CycleParameters")
                {
                    DebugLogger.LogDebug($"[TimeOfDay] Unexpected class name: {cycleName}");
                    return (0, 0);
                }

                var todComponents = Memory.ReadPtr(todSky + SDK.Offsets.TOD_Sky.TOD_Components);
                if (!MemDMA.IsValidVirtualAddress(todComponents))
                    return (0, 0);

                var todTime = Memory.ReadPtr(todComponents + SDK.Offsets.TOD_Components.TOD_Time);
                if (!MemDMA.IsValidVirtualAddress(todTime))
                    return (0, 0);

                _cachedTodTime = todTime;
                _cachedTodCycle = todCycle;
                return (todTime, todCycle);
            }
            catch
            {
                return (0, 0);
            }
        }

        private void ClearCache()
        {
            _cachedTodTime = 0;
            _cachedTodCycle = 0;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = false;
            ClearCache();
        }
    }
}
