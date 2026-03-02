namespace SDK
{
    public readonly struct IL2CPPOffsets
    {
        /// <summary>
        /// Signatures for locating the TypeInfoDefinitionTable in GameAssembly.dll.
        /// Tried in order until one succeeds.
        /// </summary>
        public static readonly string[] TypeInfoDefinitionTableSigs =
        [
            // Pattern 0: SHR rcx,4; MOV edx,8 — near table init code (scan forward for MOV [rip+disp32])
            "48 C1 E9 04 BA 08 00 00 00",
            // Pattern 1: MOV [rip+disp32],rax; MOV rax,[rip+disp32]; TEST rax,rax
            "48 89 05 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 85 C0",
            // Pattern 2 (legacy): MOV rax,[rip+disp32]; LEA r14,[rax+rsi*8]; MOV rdi,[r14]
            "48 8B 05 ?? ?? ?? ?? 4C 8D 34 F0 49 8B 3E",
        ];
    }
}
