// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Wasm;

// Structural caps checked while decoding and validating. Every count in a module is compared against
// these BEFORE anything is allocated for it, so a module that declares a billion functions costs a few
// bytes of reading, not a billion allocations. -xlinka
public sealed class WasmLimits
{
    public static readonly WasmLimits Default = new();

    public int MaxModuleBytes { get; init; } = 4 * 1024 * 1024;
    public int MaxTypes { get; init; } = 10_000;
    public int MaxFunctions { get; init; } = 10_000;
    public int MaxImports { get; init; } = 10_000;
    public int MaxExports { get; init; } = 10_000;
    public int MaxGlobals { get; init; } = 10_000;
    public int MaxTables { get; init; } = 100;
    public int MaxElementSegments { get; init; } = 10_000;
    public int MaxElementItems { get; init; } = 100_000;
    public int MaxDataSegments { get; init; } = 100_000;
    public int MaxTypeArity { get; init; } = 1_000;
    public int MaxLocalsPerFunction { get; init; } = 50_000;
    public int MaxFunctionBodyBytes { get; init; } = 1024 * 1024;
    public int MaxBlockDepth { get; init; } = 1_024;
    public int MaxBranchTableEntries { get; init; } = 65_536;
    public int MaxOperandStack { get; init; } = 65_536;
    public int MaxCustomSections { get; init; } = 1_000;
}

// What one instance may consume while it runs. Memory and tables are capped here as well as by their
// own declared maximum: whichever is smaller wins, and a grow past it simply fails the way the
// specification allows. -xlinka
public sealed class WasmInstanceLimits
{
    public static readonly WasmInstanceLimits Default = new();

    public uint MaxMemoryPages { get; init; } = 256;
    public uint MaxTableElements { get; init; } = 10_000;
    public int MaxCallDepth { get; init; } = 512;
}
