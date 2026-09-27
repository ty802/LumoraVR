// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Wasm;

// Binary format to module model. Structure only: types, indexes and code are checked by WasmValidator.
internal static class WasmDecoder
{
    private const uint Magic = 0x6D736100;
    private const uint Version = 1;

    public static WasmModule Decode(byte[] bytes, WasmLimits limits)
    {
        var module = new WasmModule { Bytes = bytes };
        var r = new WasmReader(bytes, 0, bytes.Length);

        if (r.Remaining < 4)
            throw WasmReader.Malformed("unexpected end");
        if (r.ReadFixedU32() != Magic)
            throw WasmReader.Malformed("magic header not detected");
        if (r.Remaining < 4)
            throw WasmReader.Malformed("unexpected end");
        if (r.ReadFixedU32() != Version)
            throw WasmReader.Malformed("unknown binary version");

        int lastRank = 0;
        int declaredFunctions = -1;
        int codeCount = -1;
        int customCount = 0;

        while (!r.AtEnd)
        {
            byte id = r.ReadByte();
            uint size = r.ReadU32();
            if (size > (uint)r.Remaining)
                throw WasmReader.Malformed("section size mismatch");
            int start = r.Position;
            int end = start + (int)size;
            var s = new WasmReader(bytes, start, end);

            if (id != 0)
            {
                int rank = Rank(id);
                if (rank <= lastRank)
                    throw WasmReader.Malformed("unexpected content after last section");
                lastRank = rank;
            }

            switch (id)
            {
                case 0:
                    if (++customCount > limits.MaxCustomSections)
                        throw new WasmValidationException("too many custom sections");
                    string name = s.ReadName();
                    module.Customs.Add(new WasmCustomSection(name, s.ReadBytes(s.Remaining)));
                    break;
                case 1:
                    ReadTypes(s, module, limits);
                    break;
                case 2:
                    ReadImports(s, module, limits);
                    break;
                case 3:
                {
                    int count = s.ReadCount(limits.MaxFunctions, "functions");
                    var indices = new uint[count];
                    for (int i = 0; i < count; i++)
                        indices[i] = s.ReadU32();
                    module.DefinedFunctionTypeIndices = indices;
                    declaredFunctions = count;
                    break;
                }
                case 4:
                {
                    int count = s.ReadCount(limits.MaxTables, "tables");
                    var tables = new WasmTableType[count];
                    for (int i = 0; i < count; i++)
                    {
                        var elementType = s.ReadReferenceType();
                        tables[i] = new WasmTableType(elementType, s.ReadLimits());
                    }
                    module.DefinedTables = tables;
                    break;
                }
                case 5:
                {
                    int count = s.ReadCount(limits.MaxTables, "memories");
                    var memories = new WasmMemoryType[count];
                    for (int i = 0; i < count; i++)
                        memories[i] = new WasmMemoryType(s.ReadLimits());
                    module.DefinedMemories = memories;
                    break;
                }
                case 6:
                {
                    int count = s.ReadCount(limits.MaxGlobals, "globals");
                    var globals = new WasmGlobalDefinition[count];
                    for (int i = 0; i < count; i++)
                    {
                        var type = ReadGlobalType(s);
                        globals[i] = new WasmGlobalDefinition { Type = type, Init = ReadConstExpr(s) };
                    }
                    module.DefinedGlobals = globals;
                    break;
                }
                case 7:
                {
                    int count = s.ReadCount(limits.MaxExports, "exports");
                    var exports = new WasmExport[count];
                    for (int i = 0; i < count; i++)
                    {
                        string exportName = s.ReadName();
                        byte kind = s.ReadByte();
                        if (kind > 3)
                            throw WasmReader.Malformed("malformed export kind");
                        exports[i] = new WasmExport(exportName, (WasmExternKind)kind, s.ReadU32());
                    }
                    module.ExportList = exports;
                    break;
                }
                case 8:
                    module.Start = s.ReadU32();
                    break;
                case 9:
                    ReadElements(s, module, limits);
                    break;
                case 10:
                    codeCount = ReadCode(s, module, limits);
                    break;
                case 11:
                    ReadData(s, module, limits);
                    break;
                case 12:
                    module.DataCount = s.ReadU32();
                    break;
                default:
                    throw WasmReader.Malformed("malformed section id");
            }

            if (!s.AtEnd)
                throw WasmReader.Malformed("section size mismatch");
            r.Skip((int)size);
        }

        int functions = declaredFunctions < 0 ? 0 : declaredFunctions;
        int bodies = codeCount < 0 ? 0 : codeCount;
        if (functions != bodies)
            throw WasmReader.Malformed("function and code section have inconsistent lengths");
        if (module.DataCount is uint dataCount && dataCount != (uint)module.Data.Length)
            throw WasmReader.Malformed("data count and data section have inconsistent lengths");

        BuildIndexSpaces(module);
        return module;
    }

    private static int Rank(byte id) => id switch
    {
        1 => 1,
        2 => 2,
        3 => 3,
        4 => 4,
        5 => 5,
        6 => 6,
        7 => 7,
        8 => 8,
        9 => 9,
        12 => 10,
        10 => 11,
        11 => 12,
        _ => throw WasmReader.Malformed("malformed section id"),
    };

    private static void ReadTypes(WasmReader s, WasmModule module, WasmLimits limits)
    {
        int count = s.ReadCount(limits.MaxTypes, "types");
        var types = new WasmFuncType[count];
        for (int i = 0; i < count; i++)
        {
            if (s.ReadByte() != 0x60)
                throw WasmReader.Malformed("integer representation too long");
            var parameters = ReadValueTypes(s, limits);
            var results = ReadValueTypes(s, limits);
            types[i] = new WasmFuncType(parameters, results);
        }
        module.Types = types;
    }

    private static WasmValueType[] ReadValueTypes(WasmReader s, WasmLimits limits)
    {
        int count = s.ReadCount(limits.MaxTypeArity, "parameters or results");
        var values = new WasmValueType[count];
        for (int i = 0; i < count; i++)
            values[i] = s.ReadValueType();
        return values;
    }

    private static WasmGlobalType ReadGlobalType(WasmReader s)
    {
        var valueType = s.ReadValueType();
        byte mutability = s.ReadByte();
        if (mutability > 1)
            throw WasmReader.Malformed("malformed mutability");
        return new WasmGlobalType(valueType, mutability == 1);
    }

    private static void ReadImports(WasmReader s, WasmModule module, WasmLimits limits)
    {
        int count = s.ReadCount(limits.MaxImports, "imports");
        var imports = new WasmImport[count];
        for (int i = 0; i < count; i++)
        {
            string moduleName = s.ReadName();
            string name = s.ReadName();
            byte kind = s.ReadByte();
            imports[i] = kind switch
            {
                0x00 => new WasmImport(moduleName, name, WasmExternKind.Function) { TypeIndex = s.ReadU32() },
                0x01 => new WasmImport(moduleName, name, WasmExternKind.Table) { Table = new WasmTableType(s.ReadReferenceType(), s.ReadLimits()) },
                0x02 => new WasmImport(moduleName, name, WasmExternKind.Memory) { Memory = new WasmMemoryType(s.ReadLimits()) },
                0x03 => new WasmImport(moduleName, name, WasmExternKind.Global) { Global = ReadGlobalType(s) },
                _ => throw WasmReader.Malformed("malformed import kind"),
            };
        }
        module.ImportList = imports;
    }

    internal static WasmConstExpr ReadConstExpr(WasmReader s)
    {
        byte op = s.ReadByte();
        WasmConstExpr expr;
        switch (op)
        {
            case 0x41:
                expr = new WasmConstExpr(WasmConstKind.I32, WasmValueType.I32, (uint)s.ReadS32(), 0);
                break;
            case 0x42:
                expr = new WasmConstExpr(WasmConstKind.I64, WasmValueType.I64, (ulong)s.ReadS64(), 0);
                break;
            case 0x43:
                expr = new WasmConstExpr(WasmConstKind.F32, WasmValueType.F32, s.ReadFixedU32(), 0);
                break;
            case 0x44:
                expr = new WasmConstExpr(WasmConstKind.F64, WasmValueType.F64, s.ReadFixedU64(), 0);
                break;
            case 0xD0:
                expr = new WasmConstExpr(WasmConstKind.RefNull, s.ReadReferenceType(), 0, 0);
                break;
            case 0xD2:
                expr = new WasmConstExpr(WasmConstKind.RefFunc, WasmValueType.FuncRef, 0, s.ReadU32());
                break;
            case 0x23:
                expr = new WasmConstExpr(WasmConstKind.GlobalGet, 0, 0, s.ReadU32());
                break;
            case 0x0B:
                throw new WasmValidationException("type mismatch");
            default:
                throw WasmOpcodes.IsKnown(op)
                    ? new WasmValidationException("constant expression required")
                    : WasmReader.Malformed("illegal opcode");
        }

        byte next = s.ReadByte();
        if (next != 0x0B)
        {
            throw WasmOpcodes.IsKnown(next)
                ? new WasmValidationException("constant expression required")
                : WasmReader.Malformed("illegal opcode");
        }
        return expr;
    }

    private static void ReadElements(WasmReader s, WasmModule module, WasmLimits limits)
    {
        int count = s.ReadCount(limits.MaxElementSegments, "element segments");
        var segments = new WasmElementSegment[count];
        int totalItems = 0;
        for (int i = 0; i < count; i++)
        {
            uint flags = s.ReadU32();
            var segment = new WasmElementSegment { ElementType = WasmValueType.FuncRef };
            switch (flags)
            {
                case 0:
                    segment.Mode = WasmSegmentMode.Active;
                    segment.Offset = ReadConstExpr(s);
                    segment.Items = ReadFunctionIndexItems(s, limits, ref totalItems);
                    break;
                case 1:
                    segment.Mode = WasmSegmentMode.Passive;
                    ReadElementKind(s);
                    segment.Items = ReadFunctionIndexItems(s, limits, ref totalItems);
                    break;
                case 2:
                    segment.Mode = WasmSegmentMode.Active;
                    segment.Table = s.ReadU32();
                    segment.Offset = ReadConstExpr(s);
                    ReadElementKind(s);
                    segment.Items = ReadFunctionIndexItems(s, limits, ref totalItems);
                    break;
                case 3:
                    segment.Mode = WasmSegmentMode.Declarative;
                    ReadElementKind(s);
                    segment.Items = ReadFunctionIndexItems(s, limits, ref totalItems);
                    break;
                case 4:
                    segment.Mode = WasmSegmentMode.Active;
                    segment.Offset = ReadConstExpr(s);
                    segment.Items = ReadExpressionItems(s, limits, ref totalItems);
                    break;
                case 5:
                    segment.Mode = WasmSegmentMode.Passive;
                    segment.ElementType = s.ReadReferenceType();
                    segment.Items = ReadExpressionItems(s, limits, ref totalItems);
                    break;
                case 6:
                    segment.Mode = WasmSegmentMode.Active;
                    segment.Table = s.ReadU32();
                    segment.Offset = ReadConstExpr(s);
                    segment.ElementType = s.ReadReferenceType();
                    segment.Items = ReadExpressionItems(s, limits, ref totalItems);
                    break;
                case 7:
                    segment.Mode = WasmSegmentMode.Declarative;
                    segment.ElementType = s.ReadReferenceType();
                    segment.Items = ReadExpressionItems(s, limits, ref totalItems);
                    break;
                default:
                    throw WasmReader.Malformed("malformed elements segment kind");
            }
            segments[i] = segment;
        }
        module.Elements = segments;
    }

    private static void ReadElementKind(WasmReader s)
    {
        if (s.ReadByte() != 0x00)
            throw WasmReader.Malformed("malformed element kind");
    }

    private static WasmConstExpr[] ReadFunctionIndexItems(WasmReader s, WasmLimits limits, ref int total)
    {
        int count = s.ReadCount(limits.MaxElementItems, "element items");
        total += count;
        if (total > limits.MaxElementItems)
            throw new WasmValidationException("too many element items");
        var items = new WasmConstExpr[count];
        for (int i = 0; i < count; i++)
            items[i] = new WasmConstExpr(WasmConstKind.RefFunc, WasmValueType.FuncRef, 0, s.ReadU32());
        return items;
    }

    private static WasmConstExpr[] ReadExpressionItems(WasmReader s, WasmLimits limits, ref int total)
    {
        int count = s.ReadCount(limits.MaxElementItems, "element items");
        total += count;
        if (total > limits.MaxElementItems)
            throw new WasmValidationException("too many element items");
        var items = new WasmConstExpr[count];
        for (int i = 0; i < count; i++)
            items[i] = ReadConstExpr(s);
        return items;
    }

    private static int ReadCode(WasmReader s, WasmModule module, WasmLimits limits)
    {
        int count = s.ReadCount(limits.MaxFunctions, "functions");
        var bodies = new WasmFunctionBody[count];
        var locals = new List<WasmValueType>();
        var groupCounts = new List<uint>();
        var groupTypes = new List<WasmValueType>();
        for (int i = 0; i < count; i++)
        {
            uint size = s.ReadU32();
            if (size > (uint)s.Remaining)
                throw WasmReader.Malformed("unexpected end");
            if (size > (uint)limits.MaxFunctionBodyBytes)
                throw new WasmValidationException("function body too large");
            int bodyStart = s.Position;
            int bodyEnd = bodyStart + (int)size;
            var b = new WasmReader(s.Data, bodyStart, bodyEnd);

            // Read every group before building anything: a total past 2^32 is a malformed body, and
            // that has to win over our own cap, which only says the body is too big for us.
            locals.Clear();
            groupCounts.Clear();
            groupTypes.Clear();
            uint groups = b.ReadU32();
            if (groups > (uint)b.Remaining)
                throw WasmReader.Malformed("unexpected end");
            ulong total = 0;
            for (uint g = 0; g < groups; g++)
            {
                uint n = b.ReadU32();
                total += n;
                if (total > uint.MaxValue)
                    throw WasmReader.Malformed("too many locals");
                groupCounts.Add(n);
                groupTypes.Add(b.ReadValueType());
            }
            if (total > (ulong)limits.MaxLocalsPerFunction)
                throw new WasmValidationException("too many locals");
            for (int g = 0; g < groupCounts.Count; g++)
            {
                for (uint k = 0; k < groupCounts[g]; k++)
                    locals.Add(groupTypes[g]);
            }

            bodies[i] = new WasmFunctionBody { Start = b.Position, End = bodyEnd, Locals = locals.ToArray() };
            s.Skip((int)size);
        }
        module.Bodies = bodies;
        return count;
    }

    private static void ReadData(WasmReader s, WasmModule module, WasmLimits limits)
    {
        int count = s.ReadCount(limits.MaxDataSegments, "data segments");
        var segments = new WasmDataSegment[count];
        for (int i = 0; i < count; i++)
        {
            uint flags = s.ReadU32();
            var segment = new WasmDataSegment();
            switch (flags)
            {
                case 0:
                    segment.Mode = WasmSegmentMode.Active;
                    segment.Offset = ReadConstExpr(s);
                    break;
                case 1:
                    segment.Mode = WasmSegmentMode.Passive;
                    break;
                case 2:
                    segment.Mode = WasmSegmentMode.Active;
                    segment.Memory = s.ReadU32();
                    segment.Offset = ReadConstExpr(s);
                    break;
                default:
                    throw WasmReader.Malformed("malformed data segment kind");
            }
            uint length = s.ReadU32();
            if (length > (uint)s.Remaining)
                throw WasmReader.Malformed("unexpected end of section or function");
            segment.Bytes = s.ReadBytes((int)length);
            segments[i] = segment;
        }
        module.Data = segments;
    }

    private static void BuildIndexSpaces(WasmModule module)
    {
        var functions = new List<uint>();
        var tables = new List<WasmTableType>();
        var memories = new List<WasmMemoryType>();
        var globals = new List<WasmGlobalType>();

        foreach (var import in module.ImportList)
        {
            switch (import.Kind)
            {
                case WasmExternKind.Function: functions.Add(import.TypeIndex); break;
                case WasmExternKind.Table: tables.Add(import.Table); break;
                case WasmExternKind.Memory: memories.Add(import.Memory); break;
                case WasmExternKind.Global: globals.Add(import.Global); break;
            }
        }

        module.ImportedFunctionCount = functions.Count;
        module.ImportedTableCount = tables.Count;
        module.ImportedMemoryCount = memories.Count;
        module.ImportedGlobalCount = globals.Count;

        functions.AddRange(module.DefinedFunctionTypeIndices);
        tables.AddRange(module.DefinedTables);
        memories.AddRange(module.DefinedMemories);
        foreach (var global in module.DefinedGlobals)
            globals.Add(global.Type);

        module.FunctionTypeIndices = functions.ToArray();
        module.TableTypes = tables.ToArray();
        module.MemoryTypes = memories.ToArray();
        module.GlobalTypes = globals.ToArray();
    }
}
