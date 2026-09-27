// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Wasm;

// The only way anything outside a module becomes visible to it: a module can import exactly what was
// defined here, by module name, field name and type, and nothing else. -xlinka
public sealed class WasmLinker
{
    private readonly Dictionary<(string Module, string Name), WasmExtern> _definitions = new();

    public WasmStore Store { get; }

    public WasmLinker(WasmStore store)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public void Define(string module, string name, WasmExtern value) => _definitions[(module, name)] = value;

    public WasmHostFunction DefineFunction(string module, string name, WasmFuncType type, WasmHostCallback callback, int fuelCost = 10)
    {
        var function = Store.CreateHostFunction(type, callback, fuelCost);
        Define(module, name, WasmExtern.Of(function));
        return function;
    }

    public void DefineInstance(string module, WasmInstance instance)
    {
        foreach (var export in instance.Exports)
            Define(module, export.Key, export.Value);
    }

    public WasmInstance Instantiate(WasmModule module, WasmInstanceLimits? limits = null)
    {
        var imports = new WasmExtern[module.ImportList.Length];
        for (int i = 0; i < imports.Length; i++)
        {
            var import = module.ImportList[i];
            if (!_definitions.TryGetValue((import.Module, import.Name), out var value))
                throw new WasmLinkException($"unknown import {import.Module}.{import.Name}");
            if (value.Kind != import.Kind)
                throw new WasmLinkException($"incompatible import type for {import.Module}.{import.Name}");
            if (!Matches(module, import, value))
                throw new WasmLinkException($"incompatible import type for {import.Module}.{import.Name}");
            imports[i] = value;
        }
        return WasmInstance.Create(module, Store, imports, limits ?? WasmInstanceLimits.Default);
    }

    private bool Matches(WasmModule module, WasmImport import, WasmExtern value)
    {
        switch (import.Kind)
        {
            case WasmExternKind.Function:
            {
                var function = (WasmFunction)value.Value;
                return function.Store == Store && function.Type.Equals(module.Types[import.TypeIndex]);
            }
            case WasmExternKind.Table:
            {
                var table = (WasmTable)value.Value;
                return table.ElementType == import.Table.ElementType
                    && new WasmSizeRange(table.Size, table.Max).Satisfies(import.Table.Size);
            }
            case WasmExternKind.Memory:
            {
                var memory = (WasmMemory)value.Value;
                return new WasmSizeRange(memory.Pages, memory.MaxPages).Satisfies(import.Memory.Pages);
            }
            default:
            {
                var global = (WasmGlobal)value.Value;
                return global.Type == import.Global;
            }
        }
    }
}
