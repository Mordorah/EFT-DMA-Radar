using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Prevents AFK timeout by writing a 1-week delay to the AfkMonitor.
    /// One-shot write per raid — navigates GOM → TarkovApplication → AfkMonitor.
    /// </summary>
    public sealed class AntiAfk : MemWriteFeature<AntiAfk>
    {
        private const float AFK_DELAY = 604800f; // 7 days in seconds
        private bool _applied;

        public override bool Enabled
        {
            get => App.Config.MemWrites.AntiAfkEnabled;
            set => App.Config.MemWrites.AntiAfkEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromSeconds(5);

        public override void TryApply(LocalPlayer localPlayer)
        {
            if (_applied)
                return;

            try
            {
                var gom = GameObjectManager.Get();
                var tarkovApplication = gom.FindBehaviourByClassName("TarkovApplication");

                if (!MemDMA.IsValidVirtualAddress(tarkovApplication))
                {
                    DebugLogger.LogDebug("[AntiAFK] TarkovApplication not found in GOM.");
                    return;
                }

                // TarkovApplication ObjectClass → MonoBehaviour (+0x10) → actual managed object
                var appObj = Memory.ReadPtr(tarkovApplication + ObjectClass.MonoBehaviourOffset);
                if (!MemDMA.IsValidVirtualAddress(appObj))
                    return;

                var menuOperation = Memory.ReadPtr(appObj + SDK.Offsets.TarkovApplication._menuOperation);
                if (!MemDMA.IsValidVirtualAddress(menuOperation))
                    return;

                var afkMonitor = Memory.ReadPtr(menuOperation + SDK.Offsets.MainMenuShowOperation._afkMonitor);
                if (!MemDMA.IsValidVirtualAddress(afkMonitor))
                    return;

                Memory.WriteValue(afkMonitor + SDK.Offsets.AfkMonitor.Delay, AFK_DELAY);
                _applied = true;
                DebugLogger.LogDebug("[AntiAFK] AFK delay set to 7 days.");
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[AntiAFK] Error: {ex.Message}");
            }
        }

        public override void OnRaidStart()
        {
            _applied = false;
        }
    }
}
