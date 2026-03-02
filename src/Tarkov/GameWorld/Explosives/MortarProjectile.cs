using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity;
using LoneEftDmaRadar.UI.Radar.Maps;
using LoneEftDmaRadar.UI.Skia;
using VmmSharpEx.Scatter;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Explosives
{
    /// <summary>
    /// Represents an active mortar/artillery projectile in Local Game World.
    /// Read directly from ArtilleryProjectileClient struct (no transform chain needed).
    /// </summary>
    public sealed class MortarProjectile : IExplosiveItem, IWorldEntity, IMapEntity
    {
        public static implicit operator ulong(MortarProjectile x) => x.Addr;
        private readonly ConcurrentDictionary<ulong, IExplosiveItem> _parent;

        public ulong Addr { get; }
        public bool IsActive { get; private set; }
        private Vector3 _position;
        public ref readonly Vector3 Position => ref _position;

        public MortarProjectile(ulong baseAddr, ConcurrentDictionary<ulong, IExplosiveItem> parent)
        {
            Addr = baseAddr;
            _parent = parent;
            Refresh();
            if (!IsActive)
                throw new Exception("Mortar projectile already exploded.");
        }

        /// <summary>
        /// Reads position and active state directly from ArtilleryProjectileClient.
        /// </summary>
        private void Refresh()
        {
            var data = Memory.ReadValue<ArtilleryProjectileData>(Addr, false);
            IsActive = data.IsActive;
            if (IsActive)
                _position = data.Position;
            else
                _parent.TryRemove(Addr, out _);
        }

        public void OnRefresh(VmmScatter scatter)
        {
            scatter.PrepareReadValue<ArtilleryProjectileData>(Addr);
            scatter.Completed += (sender, s) =>
            {
                if (s.ReadValue<ArtilleryProjectileData>(Addr, out var data))
                {
                    IsActive = data.IsActive;
                    if (IsActive)
                        _position = data.Position;
                    else
                        _parent.TryRemove(Addr, out _);
                }
            };
        }

        public void Draw(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            if (!IsActive)
                return;
            var circlePosition = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            var size = 5f * App.Config.UI.UIScale;
            SKPaints.ShapeOutline.StrokeWidth = SKPaints.PaintExplosives.StrokeWidth + 2f * App.Config.UI.UIScale;
            canvas.DrawCircle(circlePosition, size, SKPaints.ShapeOutline);
            canvas.DrawCircle(circlePosition, size, SKPaints.PaintExplosives);
        }

        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        private readonly struct ArtilleryProjectileData
        {
            [FieldOffset((int)Offsets.ArtilleryProjectileClient.Position)]
            public readonly Vector3 Position;

            [FieldOffset((int)Offsets.ArtilleryProjectileClient.IsActive)]
            public readonly bool IsActive;
        }
    }
}
