using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Increases magazine load/unload speed by writing to SkillManager MagDrills values.
    /// Pointer chain: LocalPlayer.Profile → Skills → MagDrillsLoadSpeed/UnloadSpeed → Value.
    /// </summary>
    public sealed class MagDrills : MemWriteFeature<MagDrills>
    {
        private const float FAST_LOAD_SPEED = 85f;
        private const float FAST_UNLOAD_SPEED = 60f;
        private const float NORMAL_LOAD_SPEED = 25f;
        private const float NORMAL_UNLOAD_SPEED = 15f;

        private bool _lastEnabledState;
        private ulong _cachedSkillManager;
        private ulong _cachedLoadPtr;
        private ulong _cachedUnloadPtr;

        public override bool Enabled
        {
            get => App.Config.MemWrites.MagDrillsEnabled;
            set => App.Config.MemWrites.MagDrillsEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(500);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                if (localPlayer == null)
                    return;

                var stateChanged = Enabled != _lastEnabledState;

                if (!Enabled)
                {
                    if (stateChanged)
                    {
                        ResetValues();
                        _lastEnabledState = false;
                    }
                    return;
                }

                ApplyMagDrills(localPlayer);

                if (stateChanged)
                    _lastEnabledState = true;
            }
            catch
            {
                ClearCache();
            }
        }

        private void ApplyMagDrills(LocalPlayer localPlayer)
        {
            var (loadAddr, unloadAddr) = GetValueAddresses(localPlayer);
            if (loadAddr == 0 || unloadAddr == 0)
                return;

            var currentLoad = Memory.ReadValue<float>(loadAddr, false);
            if (currentLoad >= 0f && currentLoad < 1000f && Math.Abs(currentLoad - FAST_LOAD_SPEED) > 0.5f)
                Memory.WriteValue(loadAddr, FAST_LOAD_SPEED);

            var currentUnload = Memory.ReadValue<float>(unloadAddr, false);
            if (currentUnload >= 0f && currentUnload < 1000f && Math.Abs(currentUnload - FAST_UNLOAD_SPEED) > 0.5f)
                Memory.WriteValue(unloadAddr, FAST_UNLOAD_SPEED);
        }

        private void ResetValues()
        {
            try
            {
                if (MemDMA.IsValidVirtualAddress(_cachedLoadPtr))
                    Memory.WriteValue(_cachedLoadPtr, NORMAL_LOAD_SPEED);
                if (MemDMA.IsValidVirtualAddress(_cachedUnloadPtr))
                    Memory.WriteValue(_cachedUnloadPtr, NORMAL_UNLOAD_SPEED);
            }
            catch { }
            ClearCache();
        }

        private (ulong loadAddr, ulong unloadAddr) GetValueAddresses(LocalPlayer localPlayer)
        {
            if (MemDMA.IsValidVirtualAddress(_cachedLoadPtr) && MemDMA.IsValidVirtualAddress(_cachedUnloadPtr))
                return (_cachedLoadPtr, _cachedUnloadPtr);

            try
            {
                var profile = Memory.ReadPtr(localPlayer + SDK.Offsets.Player.Profile, false);
                if (!MemDMA.IsValidVirtualAddress(profile))
                    return (0, 0);

                var skills = Memory.ReadPtr(profile + SDK.Offsets.Profile.Skills, false);
                if (!MemDMA.IsValidVirtualAddress(skills))
                    return (0, 0);

                _cachedSkillManager = skills;

                var loadSkill = Memory.ReadPtr(skills + SDK.Offsets.SkillManager.MagDrillsLoadSpeed, false);
                var unloadSkill = Memory.ReadPtr(skills + SDK.Offsets.SkillManager.MagDrillsUnloadSpeed, false);

                if (!MemDMA.IsValidVirtualAddress(loadSkill) || !MemDMA.IsValidVirtualAddress(unloadSkill))
                    return (0, 0);

                _cachedLoadPtr = loadSkill + SDK.Offsets.SkillValueContainer.Value;
                _cachedUnloadPtr = unloadSkill + SDK.Offsets.SkillValueContainer.Value;

                return (_cachedLoadPtr, _cachedUnloadPtr);
            }
            catch
            {
                return (0, 0);
            }
        }

        private void ClearCache()
        {
            _cachedSkillManager = 0;
            _cachedLoadPtr = 0;
            _cachedUnloadPtr = 0;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = false;
            ClearCache();
        }
    }
}
