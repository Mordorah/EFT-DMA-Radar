using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.IL2CPP;
using LoneEftDmaRadar.Tarkov.Unity;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.UI.Misc;
using SDK;
using VmmSharpEx.Options;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Interactables
{
    public sealed class WorldInteractablesManager
    {
        private static readonly HashSet<string> InteractableClassNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "WorldInteractiveObject",
            "Door",
            "Switch",
            "CardReader",
        };

        private ulong _locationSceneClass; // Cached IL2CPP class pointer (instance-level, re-resolved per session)

        private volatile List<Door> _doors = new();
        private readonly ulong _localGameWorld;
        private bool _initialized;
        private int _initAttempts;

        public IReadOnlyList<Door> Doors => _doors;

        public WorldInteractablesManager(ulong localGameWorld)
        {
            _localGameWorld = localGameWorld;
        }

        private void TryInitialize()
        {
            if (_initialized)
                return;

            _initAttempts++;
            try
            {
                // Build into a temp list, then swap atomically to avoid
                // concurrent modification with the render thread.
                var tempDoors = new List<Door>();
                int doorCount = 0;
                int totalItems = 0;

                // Path 1: Try GameWorld._world (works in online mode)
                var world = Memory.ReadValue<ulong>(_localGameWorld + Offsets.GameWorld.World, false);
                if (world != 0 && MemDMA.IsValidVirtualAddress(world))
                {
                    var arrayPtr = Memory.ReadValue<ulong>(world + Offsets.World.Interactables, false);
                    if (arrayPtr != 0 && MemDMA.IsValidVirtualAddress(arrayPtr))
                    {
                        if (_initAttempts == 1)
                            DebugLogger.LogDebug("[Interactables] Found via World path");
                        ProcessInteractablesArray(arrayPtr, tempDoors, ref doorCount, ref totalItems);
                    }
                }

                // Path 2: LocationScene.LoadedScenes — collect from ALL scenes
                if (doorCount == 0)
                    LoadFromAllLocationScenes(tempDoors, ref doorCount, ref totalItems);

                if (doorCount > 0)
                {
                    _doors = tempDoors; // Atomic reference swap — render thread sees complete list or empty
                    _initialized = true;
                }
                DebugLogger.LogDebug($"[Interactables] Loaded {doorCount}/{totalItems} doors (attempt {_initAttempts})");
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[Interactables] Init error (attempt {_initAttempts}): {ex.Message}");
            }
        }

        private void ProcessInteractablesArray(ulong arrayPtr, List<Door> target, ref int doorCount, ref int totalItems)
        {
            using var array = UnityArray<ulong>.Create(arrayPtr, useCache: false);
            foreach (var itemAddr in array)
            {
                if (itemAddr == 0 || !MemDMA.IsValidVirtualAddress(itemAddr))
                    continue;

                totalItems++;
                try
                {
                    var name = ObjectClass.ReadName(itemAddr, 64, false);
                    if (name != null && InteractableClassNames.Contains(name))
                    {
                        var door = new Door(itemAddr, name);
                        if (door.Position != Vector3.Zero)
                        {
                            target.Add(door);
                            doorCount++;
                        }
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Iterate ALL LocationScene.LoadedScenes and collect interactables from every scene.
        /// Chain: IL2CPP Class → static_fields → LoadedScenes → iterate scenes → WorldInteractiveObjects
        /// </summary>
        private void LoadFromAllLocationScenes(List<Door> target, ref int doorCount, ref int totalItems)
        {
            try
            {
                if (!IL2CPPLib.Initialized)
                {
                    if (_initAttempts <= 3)
                        DebugLogger.LogDebug($"[Interactables] IL2CPP not initialized (attempt {_initAttempts})");
                    return;
                }

                // Find LocationScene IL2CPP class (cached after first successful lookup)
                if (_locationSceneClass == 0)
                {
                    _locationSceneClass = IL2CPPLib.Class.FindClass("LocationScene");
                    DebugLogger.LogDebug($"[Interactables] LocationScene class found at 0x{_locationSceneClass:X}");
                }

                // Read static_fields pointer from IL2CPP class metadata
                var klass = Memory.ReadValue<IL2CPPLib.Class>(_locationSceneClass);
                if (klass.static_fields == 0 || !MemDMA.IsValidVirtualAddress(klass.static_fields))
                {
                    if (_initAttempts <= 3)
                        DebugLogger.LogDebug($"[Interactables] LocationScene static_fields invalid (attempt {_initAttempts})");
                    return;
                }

                // LoadedScenes is at static field offset 0x0 (first static field)
                var loadedScenesList = Memory.ReadValue<ulong>(klass.static_fields + 0x0, false);
                if (loadedScenesList == 0 || !MemDMA.IsValidVirtualAddress(loadedScenesList))
                {
                    if (_initAttempts <= 3)
                        DebugLogger.LogDebug($"[Interactables] LoadedScenes list null (attempt {_initAttempts})");
                    return;
                }

                // C# List<T>: _size at +0x18, _items (array) at +0x10
                var count = Memory.ReadValue<int>(loadedScenesList + 0x18, false);
                if (count <= 0)
                {
                    if (_initAttempts <= 3)
                        DebugLogger.LogDebug($"[Interactables] LoadedScenes empty (attempt {_initAttempts})");
                    return;
                }

                var itemsArray = Memory.ReadValue<ulong>(loadedScenesList + 0x10, false);
                if (itemsArray == 0 || !MemDMA.IsValidVirtualAddress(itemsArray))
                    return;

                // Iterate ALL scenes and collect interactables from each
                int scenesToCheck = Math.Min(count, 64);
                int scenesWithDoors = 0;
                for (int i = 0; i < scenesToCheck; i++)
                {
                    var locationScene = Memory.ReadValue<ulong>(itemsArray + 0x20 + (ulong)(i * 8), false);
                    if (locationScene == 0 || !MemDMA.IsValidVirtualAddress(locationScene))
                        continue;

                    var interactablesPtr = Memory.ReadValue<ulong>(
                        locationScene + Offsets.LocationScene.WorldInteractiveObjects, false);
                    if (interactablesPtr == 0 || !MemDMA.IsValidVirtualAddress(interactablesPtr))
                        continue;

                    // Check if this array actually has items (count at +0x18)
                    var arrayCount = Memory.ReadValue<int>(interactablesPtr + 0x18, false);
                    if (arrayCount > 0)
                    {
                        int beforeCount = doorCount;
                        ProcessInteractablesArray(interactablesPtr, target, ref doorCount, ref totalItems);
                        int sceneDoorsLoaded = doorCount - beforeCount;
                        if (sceneDoorsLoaded > 0)
                        {
                            scenesWithDoors++;
                            if (_initAttempts <= 3)
                                DebugLogger.LogDebug($"[Interactables] LocationScene[{i}]: +{sceneDoorsLoaded} doors (arrayCount={arrayCount})");
                        }
                    }
                }

                if (_initAttempts <= 3)
                    DebugLogger.LogDebug($"[Interactables] Scanned {scenesToCheck} scenes, {scenesWithDoors} had doors (attempt {_initAttempts})");
            }
            catch (Exception ex)
            {
                if (_initAttempts <= 3)
                    DebugLogger.LogDebug($"[Interactables] LocationScene static error (attempt {_initAttempts}): {ex.Message}");
            }
        }

        public void Refresh()
        {
            if (!_initialized)
            {
                if (_initAttempts < 30)
                    TryInitialize();
                return;
            }

            // Batch all door state reads into a single scatter operation
            using var scatter = Memory.CreateScatter(VmmFlags.NOCACHE);
            foreach (var door in _doors)
                door.OnRefresh(scatter);
            scatter.Execute();
        }
    }
}
