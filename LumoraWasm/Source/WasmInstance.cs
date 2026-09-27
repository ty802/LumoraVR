// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Wasm;

public sealed class WasmInstance
{
    public WasmModule Module { get; }
    public WasmStore Store { get; }
    public WasmInstanceLimits Limits { get; }

    internal readonly WasmFunction[] Functions;
    internal readonly WasmModuleFunction[] DefinedFunctions;
    internal readonly WasmTable[] Tables;
    internal WasmMemory? Memory;
    internal readonly WasmGlobal[] Globals;
    internal readonly int[] TypeIds;
    internal readonly ulong[][] ElementSegments;
    internal readonly byte[][] DataSegments;
    private readonly Dictionary<string, WasmExtern> _exports = new(StringComparer.Ordinal);

    internal byte[] MemoryBytes => Memory?.Data ?? Array.Empty<byte>();

    // Bytes of linear memory the instance has now.
    public long MemorySize => Memory is { } memory ? (long)memory.Pages * WasmMemory.PageSize : 0;

    private WasmInstance(WasmModule module, WasmStore store, WasmInstanceLimits limits)
    {
        Module = module;
        Store = store;
        Limits = limits;
        Functions = new WasmFunction[module.FunctionTypeIndices.Length];
        DefinedFunctions = new WasmModuleFunction[module.Compiled.Length];
        Tables = new WasmTable[module.TableTypes.Length];
        Globals = new WasmGlobal[module.GlobalTypes.Length];
        TypeIds = new int[module.Types.Length];
        ElementSegments = new ulong[module.Elements.Length][];
        DataSegments = new byte[module.Data.Length][];
    }

    public IEnumerable<KeyValuePair<string, WasmExtern>> Exports => _exports;

    public bool TryGetExport(string name, out WasmExtern value) => _exports.TryGetValue(name, out value);

    public WasmFunction? GetFunction(string name) => _exports.TryGetValue(name, out var e) ? e.AsFunction : null;
    public WasmGlobal? GetGlobal(string name) => _exports.TryGetValue(name, out var e) ? e.AsGlobal : null;
    public WasmMemory? GetMemory(string name) => _exports.TryGetValue(name, out var e) ? e.AsMemory : null;
    public WasmTable? GetTable(string name) => _exports.TryGetValue(name, out var e) ? e.AsTable : null;

    internal static WasmInstance Create(WasmModule module, WasmStore store, WasmExtern[] imports, WasmInstanceLimits limits)
    {
        var instance = new WasmInstance(module, store, limits);

        for (int i = 0; i < module.Types.Length; i++)
            instance.TypeIds[i] = store.InternType(module.Types[i]);

        int function = 0, table = 0, global = 0;
        for (int i = 0; i < imports.Length; i++)
        {
            var value = imports[i];
            switch (value.Kind)
            {
                case WasmExternKind.Function: instance.Functions[function++] = (WasmFunction)value.Value; break;
                case WasmExternKind.Table: instance.Tables[table++] = (WasmTable)value.Value; break;
                case WasmExternKind.Memory: instance.Memory = (WasmMemory)value.Value; break;
                case WasmExternKind.Global: instance.Globals[global++] = (WasmGlobal)value.Value; break;
            }
        }

        for (int i = 0; i < module.Compiled.Length; i++)
        {
            var defined = new WasmModuleFunction(instance, module.Compiled[i]);
            store.Register(defined);
            instance.DefinedFunctions[i] = defined;
            instance.Functions[module.ImportedFunctionCount + i] = defined;
        }

        foreach (var type in module.DefinedTables)
            instance.Tables[table++] = new WasmTable(type, limits.MaxTableElements);
        foreach (var type in module.DefinedMemories)
            instance.Memory = new WasmMemory(type, limits.MaxMemoryPages);
        foreach (var definition in module.DefinedGlobals)
            instance.Globals[global++] = new WasmGlobal(definition.Type, instance.Evaluate(definition.Init));

        foreach (var export in module.ExportList)
        {
            instance._exports[export.Name] = export.Kind switch
            {
                WasmExternKind.Function => WasmExtern.Of(instance.Functions[export.Index]),
                WasmExternKind.Table => WasmExtern.Of(instance.Tables[export.Index]),
                WasmExternKind.Memory => WasmExtern.Of(instance.Memory!),
                _ => WasmExtern.Of(instance.Globals[export.Index]),
            };
        }

        for (int i = 0; i < module.Elements.Length; i++)
        {
            var segment = module.Elements[i];
            var items = new ulong[segment.Items.Length];
            for (int k = 0; k < items.Length; k++)
                items[k] = instance.Evaluate(segment.Items[k]);
            instance.ElementSegments[i] = items;
        }
        for (int i = 0; i < module.Data.Length; i++)
            instance.DataSegments[i] = module.Data[i].Bytes;

        // Active segments are table.init and memory.init followed by a drop, in order. A segment that
        // does not fit traps the instantiation, and whatever earlier segments wrote into imported
        // tables or memory stays written.
        for (int i = 0; i < module.Elements.Length; i++)
        {
            var segment = module.Elements[i];
            if (segment.Mode == WasmSegmentMode.Active)
            {
                uint offset = (uint)instance.Evaluate(segment.Offset);
                var target = instance.Tables[segment.Table];
                var items = instance.ElementSegments[i];
                if ((ulong)offset + (ulong)items.Length > target.Size)
                    throw new WasmTrapException(WasmTrapCode.OutOfBoundsTableAccess);
                Array.Copy(items, 0, target.Elements, offset, items.Length);
            }
            if (segment.Mode != WasmSegmentMode.Passive)
                instance.ElementSegments[i] = Array.Empty<ulong>();
        }

        for (int i = 0; i < module.Data.Length; i++)
        {
            var segment = module.Data[i];
            if (segment.Mode != WasmSegmentMode.Active)
                continue;
            uint offset = (uint)instance.Evaluate(segment.Offset);
            var memory = instance.MemoryBytes;
            if ((ulong)offset + (ulong)segment.Bytes.Length > (ulong)memory.Length)
                throw new WasmTrapException(WasmTrapCode.OutOfBoundsMemoryAccess);
            Buffer.BlockCopy(segment.Bytes, 0, memory, (int)offset, segment.Bytes.Length);
            instance.DataSegments[i] = Array.Empty<byte>();
        }

        if (module.Start is uint start)
            store.Invoke(instance.Functions[start], ReadOnlySpan<ulong>.Empty, Span<ulong>.Empty, long.MaxValue, limits.MaxCallDepth);

        return instance;
    }

    private ulong Evaluate(in WasmConstExpr expr) => expr.Kind switch
    {
        WasmConstKind.RefNull => 0,
        WasmConstKind.RefFunc => (ulong)Functions[expr.Index].StoreId,
        WasmConstKind.GlobalGet => Globals[expr.Index].Value,
        _ => expr.Value,
    };

    public ulong[] Invoke(string exportName, params ulong[] arguments)
    {
        var function = GetFunction(exportName) ?? throw new ArgumentException("no exported function " + exportName);
        var results = new ulong[function.Type.Results.Length];
        Store.Invoke(function, arguments, results, long.MaxValue, Limits.MaxCallDepth);
        return results;
    }
}
