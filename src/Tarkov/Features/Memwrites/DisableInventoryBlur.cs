using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Disables inventory blur by modifying the InventoryBlur component on the FPS camera.
    /// Writes _blurCount=0 and _upsampleTexDimension=2048 to remove blur effect.
    /// </summary>
    public sealed class DisableInventoryBlur : MemWriteFeature<DisableInventoryBlur>
    {
        private const int BLUR_DISABLED_COUNT = 0;
        private const int BLUR_DEFAULT_COUNT = 5;
        private const int BLUR_DISABLED_DIMENSION = 2048;
        private const int BLUR_DEFAULT_DIMENSION = 256;

        private bool _lastEnabledState;
        private ulong _cachedBlurObject;

        public override bool Enabled
        {
            get => App.Config.MemWrites.DisableInventoryBlurEnabled;
            set => App.Config.MemWrites.DisableInventoryBlurEnabled = value;
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
                        ResetValues();
                        _lastEnabledState = false;
                    }
                    return;
                }

                ApplyDisable();

                if (stateChanged)
                    _lastEnabledState = true;
            }
            catch
            {
                ClearCache();
            }
        }

        private void ApplyDisable()
        {
            var blurObj = GetInventoryBlurObject();
            if (blurObj == 0)
                return;

            var currentBlur = Memory.ReadValue<int>(blurObj + SDK.Offsets.InventoryBlur._blurCount, false);
            if (currentBlur != BLUR_DISABLED_COUNT)
                Memory.WriteValue(blurObj + SDK.Offsets.InventoryBlur._blurCount, BLUR_DISABLED_COUNT);

            var currentDim = Memory.ReadValue<int>(blurObj + SDK.Offsets.InventoryBlur._upsampleTexDimension, false);
            if (currentDim != BLUR_DISABLED_DIMENSION)
                Memory.WriteValue(blurObj + SDK.Offsets.InventoryBlur._upsampleTexDimension, BLUR_DISABLED_DIMENSION);
        }

        private void ResetValues()
        {
            try
            {
                if (MemDMA.IsValidVirtualAddress(_cachedBlurObject))
                {
                    Memory.WriteValue(_cachedBlurObject + SDK.Offsets.InventoryBlur._blurCount, BLUR_DEFAULT_COUNT);
                    Memory.WriteValue(_cachedBlurObject + SDK.Offsets.InventoryBlur._upsampleTexDimension, BLUR_DEFAULT_DIMENSION);
                }
            }
            catch { }
            ClearCache();
        }

        private ulong GetInventoryBlurObject()
        {
            if (MemDMA.IsValidVirtualAddress(_cachedBlurObject))
                return _cachedBlurObject;

            try
            {
                var fpsCamera = MemDMA.CameraManager?.FPSCamera ?? 0;
                if (!MemDMA.IsValidVirtualAddress(fpsCamera))
                    return 0;

                // FPSCamera is a native camera object. We need to navigate to its behaviour.
                // The camera's behaviour is at the component ObjectClass chain.
                // Use MonoBehaviour.GetComponentFromBehaviour to find InventoryBlur on the same GameObject.
                var blurObjectClass = MonoBehaviour.GetComponentFromBehaviour(fpsCamera, "InventoryBlur");
                if (!MemDMA.IsValidVirtualAddress(blurObjectClass))
                    return 0;

                // ObjectClass → MonoBehaviourOffset → native managed object
                var blurObj = Memory.ReadPtr(blurObjectClass + ObjectClass.MonoBehaviourOffset);
                if (!MemDMA.IsValidVirtualAddress(blurObj))
                    return 0;

                _cachedBlurObject = blurObj;
                return blurObj;
            }
            catch
            {
                return 0;
            }
        }

        private void ClearCache()
        {
            _cachedBlurObject = 0;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = false;
            ClearCache();
        }
    }
}
