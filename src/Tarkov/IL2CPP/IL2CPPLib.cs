/*
 * IL2CPP Interop Layer
 * Ported from Lone EFT DMA Radar
 *
 * Provides an alternative method to locate GameWorld
 * by using IL2CPP metadata instead of GameObjectManager.
 */

using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.UI.Misc;
using VmmSharpEx;
using VmmSharpEx.Extensions;

namespace LoneEftDmaRadar.Tarkov.IL2CPP
{
    internal static class IL2CPPLib
    {
        #region Public API

        /// <summary>
        /// True if IL2CPPLib was successfully initialized, otherwise False.
        /// </summary>
        public static bool Initialized { get; private set; }

        public static void Init(Vmm vmm, uint pid)
        {
            try
            {
                DebugLogger.LogDebug("Initializing IL2CPP SDK...");
                _vmm = vmm;
                _pid = pid;

                if (!vmm.Map_GetModuleFromName(pid, "GameAssembly.dll", out var module))
                    throw new InvalidOperationException("Could not find GameAssembly.dll module in target process.");

                // Always resolve the type table — needed by FindClass at runtime (doors, etc.)
                Resolve_TypeInfoDefinitionTable(ref module);

                // Cache GamePlayerOwner to avoid expensive FindClass on future startups
                var cachedGpo = App.Config.DMA.CachedGamePlayerOwner;
                if (cachedGpo == 0 || !cachedGpo.IsValidUserVA())
                {
                    App.Config.DMA.CachedGamePlayerOwner = Class.FindClass("EFT.GamePlayerOwner");
                    _ = Task.Run(() => App.Config.Save());
                }

                Initialized = true;
                DebugLogger.LogDebug("IL2CPP SDK Initialized.");
            }
            catch
            {
                Reset();
                throw;
            }
        }

        /// <summary>
        /// Lookup GameWorld using IL2CPP Interop.
        /// </summary>
        /// <param name="gameWorld">GameWorld address if found.</param>
        /// <param name="map">Map identifier if found.</param>
        /// <returns>True if success, otherwise False.</returns>
        public static bool TryGetGameWorld(out ulong gameWorld, out string map)
        {
            gameWorld = default;
            map = default;
            try
            {
                if (!Initialized)
                    return false;

                var cachedGpo = App.Config.DMA.CachedGamePlayerOwner;
                if (cachedGpo == 0 || !cachedGpo.IsValidUserVA())
                    return false;

                var gamePlayerOwner = Memory.ReadValue<Class>(cachedGpo);
                var myPlayer = Memory.ReadPtr(gamePlayerOwner.static_fields + SDK.Offsets.GamePlayerOwner._myPlayer);
                gameWorld = Memory.ReadPtr(myPlayer + SDK.Offsets.Player.GameWorld);

                // Get Selected Map
                var mapPtr = Memory.ReadValue<ulong>(gameWorld + SDK.Offsets.GameWorld.LocationId);
                if (mapPtr == 0x0) // Offline Mode
                {
                    var localPlayer = Memory.ReadPtr(gameWorld + SDK.Offsets.GameWorld.MainPlayer);
                    mapPtr = Memory.ReadPtr(localPlayer + SDK.Offsets.Player.Location);
                }

                map = Memory.ReadUnicodeString(mapPtr, 128);
                DebugLogger.LogDebug("Detected Map " + map);

                if (!TarkovDataManager.MapData.ContainsKey(map)) // Also makes sure we're not in the hideout
                    throw new ArgumentException("Invalid Map ID!");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"Failed to get GameWorld via IL2CPP: {ex}");
                return false;
            }
        }

        #endregion

        private static Vmm _vmm = null!;
        private static uint _pid;
        private static int gTypeCount;
        private static ulong gTypeInfoDefinitionTable;

        static IL2CPPLib()
        {
            MemDMA.ProcessStopped += Memory_ProcessStopped;
        }

        private static void Memory_ProcessStopped(object sender, EventArgs e) => Reset();

        private static void Reset()
        {
            _vmm = default;
            _pid = default;
            Initialized = default;
            gTypeInfoDefinitionTable = default;
            gTypeCount = default;
        }

        private static void Resolve_TypeInfoDefinitionTable(ref Vmm.ModuleEntry module)
        {
            var patterns = SDK.IL2CPPOffsets.TypeInfoDefinitionTableSigs;
            ulong scanMin = module.vaBase;
            ulong scanMax = module.vaBase + module.cbImageSize;

            for (int i = 0; i < patterns.Length; i++)
            {
                try
                {
                    DebugLogger.LogDebug($"IL2CPP: Trying TypeInfoDefinitionTable pattern {i + 1}/{patterns.Length}...");
                    ulong sig = _vmm.FindSignature(_pid, patterns[i], scanMin, scanMax);
                    if (sig == 0 || !sig.IsValidUserVA())
                        continue;

                    // Find the MOV instruction that references the table pointer.
                    // Pattern 0 ("48 C1 E9 04..."): scan forward from match for "48 89 05" (MOV [rip+disp32], rax)
                    // Pattern 1 ("48 89 05..."): the MOV [rip+disp32] is at offset 0
                    // Pattern 2 ("48 8B 05..."): the MOV rax,[rip+disp32] is at offset 0
                    ulong movAddr;
                    if (i == 0)
                    {
                        movAddr = ScanForwardForMov(sig + 9, 60);
                        if (movAddr == 0)
                            continue;
                    }
                    else
                    {
                        movAddr = sig;
                    }

                    int disp32 = Memory.ReadValue<int>(movAddr + 3);
                    ulong typeDefPtrAddr = VmmSharpEx.Extensions.VmmExtensions.AddRVA(movAddr, 3 + 4, disp32);
                    gTypeInfoDefinitionTable = Memory.ReadValue<ulong>(typeDefPtrAddr);
                    if (!gTypeInfoDefinitionTable.IsValidUserVA())
                        continue;

                    // Try multiple offsets for type count (varies by IL2CPP version)
                    int[] countOffsets = { -0x10, -0x8, -0x18, -0x20 };
                    foreach (var offset in countOffsets)
                    {
                        try
                        {
                            gTypeCount = Memory.ReadValue<int>(gTypeInfoDefinitionTable + (ulong)offset) / 8;
                            if (gTypeCount > 1 && gTypeCount < 100000)
                                break;
                        }
                        catch { continue; }
                    }

                    if (gTypeCount < 1 || gTypeCount > 100000)
                        continue;

                    DebugLogger.LogDebug($"{nameof(gTypeInfoDefinitionTable)} @ 0x{gTypeInfoDefinitionTable:X} (Type Count: {gTypeCount}, Pattern: {i + 1})");
                    return;
                }
                catch (Exception ex)
                {
                    DebugLogger.LogDebug($"IL2CPP: Pattern {i + 1} failed: {ex.Message}");
                }
            }

            throw new InvalidOperationException("Could not resolve TypeInfoDefinitionTable with any signature pattern.");
        }

        /// <summary>
        /// Scan forward from an address looking for MOV [rip+disp32] instruction (48 89 05).
        /// </summary>
        private static ulong ScanForwardForMov(ulong startAddr, int maxBytes)
        {
            var chunk = _vmm.MemReadArray<byte>(_pid, startAddr, maxBytes);
            if (chunk == null)
                return 0;
            for (int j = 0; j < chunk.Length - 2; j++)
            {
                if (chunk[j] == 0x48 && chunk[j + 1] == 0x89 && chunk[j + 2] == 0x05)
                    return startAddr + (ulong)j;
            }
            return 0;
        }

        #region IL2CPP Attributes

        [Flags]
        public enum FieldAttributes : ushort
        {
            FIELD_ATTRIBUTE_FIELD_ACCESS_MASK = 0x0007,
            FIELD_ATTRIBUTE_PRIVATE_SCOPE = 0x0000,
            FIELD_ATTRIBUTE_PRIVATE = 0x0001,
            FIELD_ATTRIBUTE_FAM_AND_ASSEM = 0x0002,
            FIELD_ATTRIBUTE_ASSEMBLY = 0x0003,
            FIELD_ATTRIBUTE_FAMILY = 0x0004,
            FIELD_ATTRIBUTE_FAM_OR_ASSEM = 0x0005,
            FIELD_ATTRIBUTE_PUBLIC = 0x0006,
            FIELD_ATTRIBUTE_STATIC = 0x0010,
            FIELD_ATTRIBUTE_INIT_ONLY = 0x0020,
            FIELD_ATTRIBUTE_LITERAL = 0x0040,
            FIELD_ATTRIBUTE_NOT_SERIALIZED = 0x0080,
            FIELD_ATTRIBUTE_SPECIAL_NAME = 0x0200,
            FIELD_ATTRIBUTE_PINVOKE_IMPL = 0x2000,
        }

        [Flags]
        public enum MethodAttributes : ushort
        {
            METHOD_ATTRIBUTE_MEMBER_ACCESS_MASK = 0x0007,
            METHOD_ATTRIBUTE_PRIVATE_SCOPE = 0x0000,
            METHOD_ATTRIBUTE_PRIVATE = 0x0001,
            METHOD_ATTRIBUTE_FAM_AND_ASSEM = 0x0002,
            METHOD_ATTRIBUTE_ASSEM = 0x0003,
            METHOD_ATTRIBUTE_FAMILY = 0x0004,
            METHOD_ATTRIBUTE_FAM_OR_ASSEM = 0x0005,
            METHOD_ATTRIBUTE_PUBLIC = 0x0006,
            METHOD_ATTRIBUTE_STATIC = 0x0010,
            METHOD_ATTRIBUTE_FINAL = 0x0020,
            METHOD_ATTRIBUTE_VIRTUAL = 0x0040,
            METHOD_ATTRIBUTE_HIDE_BY_SIG = 0x0080,
            METHOD_ATTRIBUTE_VTABLE_LAYOUT_MASK = 0x0100,
            METHOD_ATTRIBUTE_REUSE_SLOT = 0x0000,
            METHOD_ATTRIBUTE_NEW_SLOT = 0x0100,
            METHOD_ATTRIBUTE_STRICT = 0x0200,
            METHOD_ATTRIBUTE_ABSTRACT = 0x0400,
            METHOD_ATTRIBUTE_SPECIAL_NAME = 0x0800,
        }

        [Flags]
        public enum TypeAttributes : uint
        {
            TYPE_ATTRIBUTE_VISIBILITY_MASK = 0x00000007,
            TYPE_ATTRIBUTE_NOT_PUBLIC = 0x00000000,
            TYPE_ATTRIBUTE_PUBLIC = 0x00000001,
            TYPE_ATTRIBUTE_NESTED_PUBLIC = 0x00000002,
            TYPE_ATTRIBUTE_NESTED_PRIVATE = 0x00000003,
            TYPE_ATTRIBUTE_NESTED_FAMILY = 0x00000004,
            TYPE_ATTRIBUTE_NESTED_ASSEMBLY = 0x00000005,
            TYPE_ATTRIBUTE_NESTED_FAM_AND_ASSEM = 0x00000006,
            TYPE_ATTRIBUTE_NESTED_FAM_OR_ASSEM = 0x00000007,
            TYPE_ATTRIBUTE_LAYOUT_MASK = 0x00000018,
            TYPE_ATTRIBUTE_AUTO_LAYOUT = 0x00000000,
            TYPE_ATTRIBUTE_SEQUENTIAL_LAYOUT = 0x00000008,
            TYPE_ATTRIBUTE_EXPLICIT_LAYOUT = 0x00000010,
            TYPE_ATTRIBUTE_CLASS_SEMANTIC_MASK = 0x00000020,
            TYPE_ATTRIBUTE_CLASS = 0x00000000,
            TYPE_ATTRIBUTE_INTERFACE = 0x00000020,
            TYPE_ATTRIBUTE_ABSTRACT = 0x00000080,
            TYPE_ATTRIBUTE_SEALED = 0x00000100,
            TYPE_ATTRIBUTE_SPECIAL_NAME = 0x00000400,
            TYPE_ATTRIBUTE_IMPORT = 0x00001000,
            TYPE_ATTRIBUTE_SERIALIZABLE = 0x00002000,
            TYPE_ATTRIBUTE_STRING_FORMAT_MASK = 0x00030000,
            TYPE_ATTRIBUTE_ANSI_CLASS = 0x00000000,
            TYPE_ATTRIBUTE_UNICODE_CLASS = 0x00010000,
            TYPE_ATTRIBUTE_AUTO_CLASS = 0x00020000,
            TYPE_ATTRIBUTE_CUSTOM_FORMAT_CLASS = 0x00030000,
            TYPE_ATTRIBUTE_CUSTOM_FORMAT_MASK = 0x00C00000,
            TYPE_ATTRIBUTE_BEFORE_FIELD_INIT = 0x00100000,
            TYPE_ATTRIBUTE_FORWARDER = 0x00200000,
        }

        #endregion

        #region IL2CPP Structures

        [StructLayout(LayoutKind.Explicit)]
        public readonly struct Type
        {
            [FieldOffset(0x00)] public readonly ulong data;
            [FieldOffset(0x08)] public readonly ushort attrs;

            public readonly string GetName()
            {
                if (!data.IsValidUserVA())
                    return string.Empty;
                var klass = Memory.ReadValue<Class>(data);
                return klass.GetName();
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        public readonly struct Class
        {
            [FieldOffset(0x00)] public readonly ulong image;
            [FieldOffset(0x08)] public readonly ulong gc_desc;
            [FieldOffset(0x10)] public readonly ulong name;
            [FieldOffset(0x18)] public readonly ulong namespaze;
            [FieldOffset(0x20)] public readonly Type type;
            [FieldOffset(0x58)] public readonly ulong parent;
            [FieldOffset(0x80)] public readonly ulong fields;
            [FieldOffset(0x88)] public readonly ulong events;
            [FieldOffset(0x90)] public readonly ulong properties;
            [FieldOffset(0x98)] public readonly ulong methods;
            [FieldOffset(0x118)] public readonly uint flags;
            [FieldOffset(0x11C)] public readonly uint token;
            [FieldOffset(0x120)] public readonly ushort method_count;
            [FieldOffset(0x122)] public readonly ushort property_count;
            [FieldOffset(0x124)] public readonly ushort field_count;
            [FieldOffset(0x126)] public readonly ushort event_count;
            [FieldOffset(0xB8)] public readonly ulong static_fields;
            [FieldOffset(0xC8)] public readonly ulong typeHierarchy;

            public static ulong FindClass(string name)
            {
                using var ptrs = _vmm.MemReadPooled<ulong>(_pid, gTypeInfoDefinitionTable, gTypeCount) ??
                    throw new InvalidOperationException("Failed to read type definition table.");
                using var scatter = Memory.CreateScatterMap();
                var rd1 = scatter.AddRound();
                var rd2 = scatter.AddRound();
                ulong result = default;
                foreach (var ptr in ptrs.Memory.Span)
                {
                    _ = rd1.PrepareReadValue<Class>(ptr);
                    rd1.Completed += (_, s1) =>
                    {
                        if (s1.ReadValue<Class>(ptr, out var klass))
                        {
                            _ = rd2.PrepareRead(klass.namespaze, 128);
                            _ = rd2.PrepareRead(klass.name, 128);
                            rd2.Completed += (_, s2) =>
                            {
                                if (s2.ReadString(klass.namespaze, 128, Encoding.ASCII) is string ns &&
                                    s2.ReadString(klass.name, 128, Encoding.ASCII) is string n)
                                {
                                    string fullName = string.IsNullOrEmpty(ns) ? n : $"{ns}.{n}";
                                    if (fullName.Equals(name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        result = ptr;
                                    }
                                }
                            };
                        }
                    };
                }
                scatter.Execute();
                result.ThrowIfInvalidUserVA(nameof(result));
                return result;
            }

            public readonly string GetName() => Memory.ReadUtf8String(name, 256);

            public readonly string GetNamespace()
            {
                if (!namespaze.IsValidUserVA())
                    return string.Empty;
                return Memory.ReadUtf8String(namespaze, 256);
            }

            public override string ToString()
            {
                string ns = GetNamespace();
                if (string.IsNullOrEmpty(ns))
                    return GetName();
                return $"{ns}.{GetName()}";
            }
        }

        #endregion
    }
}
