using LoneEftDmaRadar.DMA;

namespace LoneEftDmaRadar.Tarkov.Unity.Structures
{
    [StructLayout(LayoutKind.Explicit)]
    public readonly struct MonoBehaviour // Behaviour : Component : EditorExtension : Object
    {
        [FieldOffset((int)UnitySDK.UnityOffsets.Component_ObjectClassOffset)]
        public readonly ulong ObjectClass; // m_Object
        [FieldOffset((int)UnitySDK.UnityOffsets.Component_GameObjectOffset)]
        public readonly ulong GameObject; // m_GameObject

        /// <summary>
        /// Return the game object of this MonoBehaviour.
        /// </summary>
        /// <returns>GameObject struct.</returns>
        public readonly GameObject GetGameObject() =>
            Memory.ReadValue<GameObject>(ObjectClass);

        /// <summary>
        /// Gets a component's ObjectClass from a Behaviour's sibling components by class name.
        /// Navigates: behaviour -> GameObject -> Components DynamicArray -> match by ObjectClass name.
        /// </summary>
        /// <param name="behaviour">A behaviour/component address on the target GameObject.</param>
        /// <param name="className">Class name to find (e.g. "InventoryBlur", "MBOIT_Scattering").</param>
        /// <returns>The ObjectClass address of the matching component, or 0 if not found.</returns>
        public static ulong GetComponentFromBehaviour(ulong behaviour, string className)
        {
            // behaviour native object -> Component_GameObjectOffset -> native GameObject
            var gameObjectPtr = Memory.ReadPtr(behaviour + UnitySDK.UnityOffsets.Component_GameObjectOffset);
            if (!MemDMA.IsValidVirtualAddress(gameObjectPtr))
                return 0;

            // Components DynamicArray starts at gameObject + 0x58
            // Layout: {ArrayBase(+0x0), MemLabelId(+0x8), Size(+0x10), Capacity(+0x18)}
            var componentsBase = gameObjectPtr + UnitySDK.UnityOffsets.GameObject_ComponentsOffset;
            var arrayBase = Memory.ReadValue<ulong>(componentsBase);
            var count = Memory.ReadValue<int>(componentsBase + 0x10);

            if (!MemDMA.IsValidVirtualAddress(arrayBase) || count <= 0 || count > 256)
                return 0;

            // Each entry is 16 bytes: {padding/behaviour(+0x0), ObjectClass(+0x8)}
            for (int i = 0; i < count; i++)
            {
                var entryAddr = arrayBase + (ulong)(i * 0x10);
                var componentObjectClass = Memory.ReadValue<ulong>(entryAddr + 0x8);

                if (!MemDMA.IsValidVirtualAddress(componentObjectClass))
                    continue;

                var name = Structures.ObjectClass.ReadName(componentObjectClass, 128, false);
                if (name != null && name.Equals(className, StringComparison.OrdinalIgnoreCase))
                    return componentObjectClass;
            }

            return 0;
        }
    }
}
