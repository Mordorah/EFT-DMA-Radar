using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.Tarkov.Unity.Structures;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Speeds up weapon animations (reload, chamber, etc.) and aiming.
    /// Writes animator speed and aiming speed on the ProceduralWeaponAnimation.
    /// </summary>
    public sealed class FastWeaponOps : MemWriteFeature<FastWeaponOps>
    {
        private const float FAST_SPEED = 4.0f;
        private const float FAST_AIMING_SPEED = 9999f;
        private const float NORMAL_SPEED = 1.0f;

        private bool _lastEnabledState;
        private ulong _cachedAnimator;

        public override bool Enabled
        {
            get => App.Config.MemWrites.FastWeaponOpsEnabled;
            set => App.Config.MemWrites.FastWeaponOpsEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(100);

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
                        ResetValues(localPlayer);
                        _lastEnabledState = false;
                    }
                    return;
                }

                ApplyFastOps(localPlayer);

                if (stateChanged)
                    _lastEnabledState = true;
            }
            catch
            {
                ClearCache();
            }
        }

        private void ApplyFastOps(LocalPlayer localPlayer)
        {
            // Write animator speed
            var animator = GetAnimator(localPlayer);
            if (MemDMA.IsValidVirtualAddress(animator))
            {
                var currentSpeed = Memory.ReadValue<float>(animator + SDK.Offsets.UnityAnimator.Speed, false);
                if (currentSpeed > 0f && currentSpeed < 100f && Math.Abs(currentSpeed - FAST_SPEED) > 0.01f)
                    Memory.WriteValue(animator + SDK.Offsets.UnityAnimator.Speed, FAST_SPEED);
            }

            // Write aiming speed
            var pwa = localPlayer.PWA;
            if (MemDMA.IsValidVirtualAddress(pwa))
            {
                var currentAimSpeed = Memory.ReadValue<float>(pwa + SDK.Offsets.ProceduralWeaponAnimation._aimingSpeed, false);
                if (currentAimSpeed > 0f && currentAimSpeed < 100000f && Math.Abs(currentAimSpeed - FAST_AIMING_SPEED) > 0.01f)
                    Memory.WriteValue(pwa + SDK.Offsets.ProceduralWeaponAnimation._aimingSpeed, FAST_AIMING_SPEED);
            }
        }

        private void ResetValues(LocalPlayer localPlayer)
        {
            try
            {
                var animator = GetAnimator(localPlayer);
                if (MemDMA.IsValidVirtualAddress(animator))
                    Memory.WriteValue(animator + SDK.Offsets.UnityAnimator.Speed, NORMAL_SPEED);

                var pwa = localPlayer.PWA;
                if (MemDMA.IsValidVirtualAddress(pwa))
                    Memory.WriteValue(pwa + SDK.Offsets.ProceduralWeaponAnimation._aimingSpeed, NORMAL_SPEED);
            }
            catch { }
            ClearCache();
        }

        private ulong GetAnimator(LocalPlayer localPlayer)
        {
            if (MemDMA.IsValidVirtualAddress(_cachedAnimator))
                return _cachedAnimator;

            try
            {
                // Player._animators → array pointer → read as MemArray
                var animatorsPtr = Memory.ReadPtr(localPlayer + SDK.Offsets.Player._animators, false);
                if (!MemDMA.IsValidVirtualAddress(animatorsPtr))
                    return 0;

                using var animators = UnityArray<ulong>.Create(animatorsPtr, false);
                if (animators.Count <= 1)
                    return 0;

                // Index 1 = body animator
                var bodyAnimator = animators.ElementAtOrDefault(1);
                if (!MemDMA.IsValidVirtualAddress(bodyAnimator))
                    return 0;

                // BodyAnimator → UnityAnimator (+0x10) → ObjectClass.MonoBehaviourOffset (+0x10) → native Animator
                var unityAnimator = Memory.ReadPtr(bodyAnimator + SDK.Offsets.BodyAnimator.UnityAnimator, false);
                if (!MemDMA.IsValidVirtualAddress(unityAnimator))
                    return 0;

                var nativeAnimator = Memory.ReadPtr(unityAnimator + ObjectClass.MonoBehaviourOffset, false);
                if (!MemDMA.IsValidVirtualAddress(nativeAnimator))
                    return 0;

                _cachedAnimator = nativeAnimator;
                return nativeAnimator;
            }
            catch
            {
                return 0;
            }
        }

        private void ClearCache()
        {
            _cachedAnimator = 0;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = false;
            ClearCache();
        }
    }
}
