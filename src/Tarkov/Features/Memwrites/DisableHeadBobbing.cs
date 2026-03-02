using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.IL2CPP;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Disables head bobbing by writing 0.0f to the BSGGameSetting value.
    /// Uses IL2CPP class resolution to find GameSettingsGroup static fields.
    /// </summary>
    public sealed class DisableHeadBobbing : MemWriteFeature<DisableHeadBobbing>
    {
        private const float DISABLED_VALUE = 0.0f;
        private const float DEFAULT_VALUE = 0.2f;
        private const uint HEADBOBBING_STATIC_OFFSET = 0x68;

        private bool _lastEnabledState;
        private ulong _cachedValueAddr;

        public override bool Enabled
        {
            get => App.Config.MemWrites.DisableHeadBobbingEnabled;
            set => App.Config.MemWrites.DisableHeadBobbingEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromSeconds(1);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                var stateChanged = Enabled != _lastEnabledState;

                if (!Enabled)
                {
                    if (stateChanged)
                    {
                        ResetValue();
                        _lastEnabledState = false;
                    }
                    return;
                }

                ApplyDisable();

                if (stateChanged)
                    _lastEnabledState = true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[DisableHeadBobbing] Error: {ex.Message}");
                ClearCache();
            }
        }

        private void ApplyDisable()
        {
            var valueAddr = GetHeadBobbingValueAddr();
            if (valueAddr == 0)
                return;

            var current = Memory.ReadValue<float>(valueAddr, false);
            if (current >= -1f && current <= 5f && Math.Abs(current - DISABLED_VALUE) > 0.001f)
                Memory.WriteValue(valueAddr, DISABLED_VALUE);
        }

        private void ResetValue()
        {
            try
            {
                if (MemDMA.IsValidVirtualAddress(_cachedValueAddr))
                    Memory.WriteValue(_cachedValueAddr, DEFAULT_VALUE);
            }
            catch { }
            ClearCache();
        }

        private ulong GetHeadBobbingValueAddr()
        {
            if (MemDMA.IsValidVirtualAddress(_cachedValueAddr))
                return _cachedValueAddr;

            try
            {
                if (!IL2CPPLib.Initialized)
                    return 0;

                // Find GameSettingsGroup class via IL2CPP type table
                var klassPtr = IL2CPPLib.Class.FindClass("EFT.Settings.Game.GameSettingsGroup");
                if (!MemDMA.IsValidVirtualAddress(klassPtr))
                    return 0;

                // Read class struct to get static_fields
                var klass = Memory.ReadValue<IL2CPPLib.Class>(klassPtr);
                if (!MemDMA.IsValidVirtualAddress(klass.static_fields))
                    return 0;

                // HeadBobbing BSGGameSetting at static_fields + 0x68
                var headBobbingPtr = Memory.ReadPtr(klass.static_fields + HEADBOBBING_STATIC_OFFSET);
                if (!MemDMA.IsValidVirtualAddress(headBobbingPtr))
                    return 0;

                // BSGGameSetting → ValueClass → Value
                var valueClass = Memory.ReadPtr(headBobbingPtr + SDK.Offsets.BSGGameSetting.ValueClass);
                if (!MemDMA.IsValidVirtualAddress(valueClass))
                    return 0;

                _cachedValueAddr = valueClass + SDK.Offsets.BSGGameSettingValueClass.Value;
                return _cachedValueAddr;
            }
            catch
            {
                return 0;
            }
        }

        private void ClearCache()
        {
            _cachedValueAddr = 0;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = false;
            ClearCache();
        }
    }
}
