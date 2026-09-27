// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Text;

namespace Lumora.Wasm;

internal sealed class WasmByteWriter
{
    private byte[] _buffer = new byte[256];
    private int _length;

    public int Length => _length;

    private void Ensure(int extra)
    {
        if (_length + extra <= _buffer.Length)
            return;
        Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + extra));
    }

    public void Byte(byte value)
    {
        Ensure(1);
        _buffer[_length++] = value;
    }

    public void U32(uint value)
    {
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
                b |= 0x80;
            Byte(b);
        }
        while (value != 0);
    }

    public void S32(int value) => S64(value);

    public void S64(long value)
    {
        while (true)
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            bool done = (value == 0 && (b & 0x40) == 0) || (value == -1 && (b & 0x40) != 0);
            if (!done)
                b |= 0x80;
            Byte(b);
            if (done)
                return;
        }
    }

    public void Fixed32(uint value)
    {
        Ensure(4);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(_length, 4), value);
        _length += 4;
    }

    public void Fixed64(ulong value)
    {
        Ensure(8);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(_buffer.AsSpan(_length, 8), value);
        _length += 8;
    }

    public void Bytes(ReadOnlySpan<byte> bytes)
    {
        Ensure(bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_length));
        _length += bytes.Length;
    }

    public void Name(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        U32((uint)bytes.Length);
        Bytes(bytes);
    }

    public ReadOnlySpan<byte> Span => _buffer.AsSpan(0, _length);

    public byte[] ToArray() => _buffer.AsSpan(0, _length).ToArray();
}

// A constant initializer for a global or segment offset.
public readonly struct WasmConstInit
{
    internal readonly byte[] Encoded;

    private WasmConstInit(byte[] encoded)
    {
        Encoded = encoded;
    }

    private static WasmConstInit Of(Action<WasmByteWriter> write)
    {
        var w = new WasmByteWriter();
        write(w);
        w.Byte(WasmOp.End);
        return new WasmConstInit(w.ToArray());
    }

    public static WasmConstInit I32(int value) => Of(w => { w.Byte(WasmOp.I32Const); w.S32(value); });
    public static WasmConstInit I64(long value) => Of(w => { w.Byte(WasmOp.I64Const); w.S64(value); });
    public static WasmConstInit F32(float value) => Of(w => { w.Byte(WasmOp.F32Const); w.Fixed32(BitConverter.SingleToUInt32Bits(value)); });
    public static WasmConstInit F64(double value) => Of(w => { w.Byte(WasmOp.F64Const); w.Fixed64(BitConverter.DoubleToUInt64Bits(value)); });
    public static WasmConstInit RefNull(WasmValueType type) => Of(w => { w.Byte(WasmOp.RefNull); w.Byte((byte)type); });
    public static WasmConstInit RefFunc(int function) => Of(w => { w.Byte(WasmOp.RefFunc); w.U32((uint)function); });
    public static WasmConstInit GlobalGet(int global) => Of(w => { w.Byte(WasmOp.GlobalGet); w.U32((uint)global); });
}

// Writes modules. Imports have to be added before anything they would share an index space with, the
// same order the binary format numbers them in. Source marks recorded in function bodies come out as a
// "lumora.srcmap" custom section with absolute byte offsets, the ones a trap reports.
public sealed class WasmModuleBuilder
{
    private readonly List<WasmFuncType> _types = new();
    private readonly Dictionary<WasmFuncType, int> _typeIndices = new();
    private readonly WasmByteWriter _imports = new();
    private int _importCount;
    private int _importedFunctions;
    private int _importedTables;
    private int _importedMemories;
    private int _importedGlobals;
    private readonly List<WasmFunctionBuilder> _functions = new();
    private readonly WasmByteWriter _tables = new();
    private int _tableCount;
    private readonly WasmByteWriter _memories = new();
    private int _memoryCount;
    private readonly WasmByteWriter _globals = new();
    private int _globalCount;
    private readonly WasmByteWriter _exports = new();
    private int _exportCount;
    private int _start = -1;
    private readonly WasmByteWriter _elements = new();
    private int _elementCount;
    private readonly WasmByteWriter _data = new();
    private int _dataCount;
    private bool _usesDataIndices;
    private readonly List<(string Name, byte[] Bytes)> _customs = new();

    public int FunctionCount => _importedFunctions + _functions.Count;

    public int AddType(WasmFuncType type)
    {
        if (!_typeIndices.TryGetValue(type, out int index))
        {
            index = _types.Count;
            _types.Add(type);
            _typeIndices.Add(type, index);
        }
        return index;
    }

    private void ImportHeader(string module, string name, byte kind)
    {
        _imports.Name(module);
        _imports.Name(name);
        _imports.Byte(kind);
        _importCount++;
    }

    public int ImportFunction(string module, string name, WasmFuncType type)
    {
        if (_functions.Count > 0)
            throw new InvalidOperationException("imports must come before defined functions");
        int typeIndex = AddType(type);
        ImportHeader(module, name, 0x00);
        _imports.U32((uint)typeIndex);
        return _importedFunctions++;
    }

    public int ImportTable(string module, string name, WasmValueType elementType, WasmSizeRange size)
    {
        if (_tableCount > 0)
            throw new InvalidOperationException("imports must come before defined tables");
        ImportHeader(module, name, 0x01);
        _imports.Byte((byte)elementType);
        WriteLimits(_imports, size);
        return _importedTables++;
    }

    public int ImportMemory(string module, string name, WasmSizeRange pages)
    {
        if (_memoryCount > 0)
            throw new InvalidOperationException("imports must come before defined memories");
        ImportHeader(module, name, 0x02);
        WriteLimits(_imports, pages);
        return _importedMemories++;
    }

    public int ImportGlobal(string module, string name, WasmGlobalType type)
    {
        if (_globalCount > 0)
            throw new InvalidOperationException("imports must come before defined globals");
        ImportHeader(module, name, 0x03);
        _imports.Byte((byte)type.ValueType);
        _imports.Byte(type.Mutable ? (byte)1 : (byte)0);
        return _importedGlobals++;
    }

    public WasmFunctionBuilder AddFunction(WasmFuncType type)
    {
        var function = new WasmFunctionBuilder(this, _importedFunctions + _functions.Count, type, AddType(type));
        _functions.Add(function);
        return function;
    }

    public int AddTable(WasmValueType elementType, uint min, uint? max = null)
    {
        _tables.Byte((byte)elementType);
        WriteLimits(_tables, new WasmSizeRange(min, max));
        return _importedTables + _tableCount++;
    }

    public int AddMemory(uint minPages, uint? maxPages = null)
    {
        WriteLimits(_memories, new WasmSizeRange(minPages, maxPages));
        return _importedMemories + _memoryCount++;
    }

    public int AddGlobal(WasmGlobalType type, WasmConstInit init)
    {
        _globals.Byte((byte)type.ValueType);
        _globals.Byte(type.Mutable ? (byte)1 : (byte)0);
        _globals.Bytes(init.Encoded);
        return _importedGlobals + _globalCount++;
    }

    public void Export(string name, WasmExternKind kind, int index)
    {
        _exports.Name(name);
        _exports.Byte((byte)kind);
        _exports.U32((uint)index);
        _exportCount++;
    }

    public void SetStart(int function) => _start = function;

    public int AddActiveData(WasmConstInit offset, byte[] bytes)
    {
        _data.U32(0);
        _data.Bytes(offset.Encoded);
        _data.U32((uint)bytes.Length);
        _data.Bytes(bytes);
        return _dataCount++;
    }

    public int AddPassiveData(byte[] bytes)
    {
        _data.U32(1);
        _data.U32((uint)bytes.Length);
        _data.Bytes(bytes);
        return _dataCount++;
    }

    public void AddActiveElements(WasmConstInit offset, IReadOnlyList<int> functions)
    {
        _elements.U32(0);
        _elements.Bytes(offset.Encoded);
        _elements.U32((uint)functions.Count);
        foreach (int f in functions)
            _elements.U32((uint)f);
        _elementCount++;
    }

    // A declarative segment: the only effect is that ref.func may name these functions.
    public void DeclareFunctionReferences(IReadOnlyList<int> functions)
    {
        _elements.U32(3);
        _elements.Byte(0x00);
        _elements.U32((uint)functions.Count);
        foreach (int f in functions)
            _elements.U32((uint)f);
        _elementCount++;
    }

    public void AddCustomSection(string name, byte[] bytes) => _customs.Add((name, bytes));

    internal void NoteDataIndexUse() => _usesDataIndices = true;

    private static void WriteLimits(WasmByteWriter w, WasmSizeRange range)
    {
        if (range.Max is uint max)
        {
            w.Byte(0x01);
            w.U32(range.Min);
            w.U32(max);
        }
        else
        {
            w.Byte(0x00);
            w.U32(range.Min);
        }
    }

    private static void Section(WasmByteWriter output, byte id, WasmByteWriter content, int count)
    {
        var body = new WasmByteWriter();
        body.U32((uint)count);
        body.Bytes(content.Span);
        output.Byte(id);
        output.U32((uint)body.Length);
        output.Bytes(body.Span);
    }

    public byte[] ToBytes()
    {
        var output = new WasmByteWriter();
        output.Fixed32(0x6D736100);
        output.Fixed32(1);

        if (_types.Count > 0)
        {
            var types = new WasmByteWriter();
            foreach (var type in _types)
            {
                types.Byte(0x60);
                types.U32((uint)type.Parameters.Length);
                foreach (var p in type.Parameters)
                    types.Byte((byte)p);
                types.U32((uint)type.Results.Length);
                foreach (var r in type.Results)
                    types.Byte((byte)r);
            }
            Section(output, 1, types, _types.Count);
        }
        if (_importCount > 0)
            Section(output, 2, _imports, _importCount);
        if (_functions.Count > 0)
        {
            var declarations = new WasmByteWriter();
            foreach (var f in _functions)
                declarations.U32((uint)f.TypeIndex);
            Section(output, 3, declarations, _functions.Count);
        }
        if (_tableCount > 0)
            Section(output, 4, _tables, _tableCount);
        if (_memoryCount > 0)
            Section(output, 5, _memories, _memoryCount);
        if (_globalCount > 0)
            Section(output, 6, _globals, _globalCount);
        if (_exportCount > 0)
            Section(output, 7, _exports, _exportCount);
        if (_start >= 0)
        {
            var start = new WasmByteWriter();
            start.U32((uint)_start);
            output.Byte(8);
            output.U32((uint)start.Length);
            output.Bytes(start.Span);
        }
        if (_elementCount > 0)
            Section(output, 9, _elements, _elementCount);
        if (_usesDataIndices || _dataCount > 0)
        {
            var count = new WasmByteWriter();
            count.U32((uint)_dataCount);
            output.Byte(12);
            output.U32((uint)count.Length);
            output.Bytes(count.Span);
        }

        var marks = new List<(int Offset, int Tag)>();
        if (_functions.Count > 0)
        {
            // The code section is laid out twice: once to learn where every body lands, then for real.
            var bodies = new List<(byte[] Bytes, int CodeStart)>();
            foreach (var f in _functions)
                bodies.Add((f.Encode(out int codeStart), codeStart));

            var content = new WasmByteWriter();
            content.U32((uint)bodies.Count);
            var bodyStarts = new int[bodies.Count];
            for (int i = 0; i < bodies.Count; i++)
            {
                content.U32((uint)bodies[i].Bytes.Length);
                bodyStarts[i] = content.Length;
                content.Bytes(bodies[i].Bytes);
            }
            output.Byte(10);
            output.U32((uint)content.Length);
            int sectionStart = output.Length;
            output.Bytes(content.Span);

            for (int i = 0; i < _functions.Count; i++)
            {
                int codeBase = sectionStart + bodyStarts[i] + bodies[i].CodeStart;
                foreach (var mark in _functions[i].Marks)
                    marks.Add((codeBase + mark.BodyOffset, mark.Tag));
            }
        }

        if (_dataCount > 0)
            Section(output, 11, _data, _dataCount);

        foreach (var (name, bytes) in _customs)
        {
            var custom = new WasmByteWriter();
            custom.Name(name);
            custom.Bytes(bytes);
            output.Byte(0);
            output.U32((uint)custom.Length);
            output.Bytes(custom.Span);
        }

        if (marks.Count > 0)
        {
            marks.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            var map = new WasmByteWriter();
            map.Name(WasmSourceMap.SectionName);
            map.U32((uint)marks.Count);
            foreach (var (offset, tag) in marks)
            {
                map.U32((uint)offset);
                map.U32((uint)tag);
            }
            output.Byte(0);
            output.U32((uint)map.Length);
            output.Bytes(map.Span);
        }

        return output.ToArray();
    }
}

public sealed class WasmFunctionBuilder
{
    private readonly WasmModuleBuilder _module;
    private readonly List<WasmValueType> _locals = new();
    private readonly WasmByteWriter _code = new();
    internal readonly List<(int BodyOffset, int Tag)> Marks = new();

    public int Index { get; }
    public WasmFuncType Type { get; }
    internal int TypeIndex { get; }

    internal WasmFunctionBuilder(WasmModuleBuilder module, int index, WasmFuncType type, int typeIndex)
    {
        _module = module;
        Index = index;
        Type = type;
        TypeIndex = typeIndex;
    }

    public int AddLocal(WasmValueType type)
    {
        _locals.Add(type);
        return Type.Parameters.Length + _locals.Count - 1;
    }

    // Everything emitted from here to the next mark belongs to `tag` in the source map.
    public void Mark(int tag) => Marks.Add((_code.Length, tag));

    public WasmFunctionBuilder Op(byte opcode)
    {
        _code.Byte(opcode);
        return this;
    }

    public WasmFunctionBuilder Prefixed(uint subOpcode)
    {
        _code.Byte(WasmOp.Prefix);
        _code.U32(subOpcode);
        return this;
    }

    public WasmFunctionBuilder I32Const(int value) { _code.Byte(WasmOp.I32Const); _code.S32(value); return this; }
    public WasmFunctionBuilder I64Const(long value) { _code.Byte(WasmOp.I64Const); _code.S64(value); return this; }
    public WasmFunctionBuilder F32Const(float value) { _code.Byte(WasmOp.F32Const); _code.Fixed32(BitConverter.SingleToUInt32Bits(value)); return this; }
    public WasmFunctionBuilder F64Const(double value) { _code.Byte(WasmOp.F64Const); _code.Fixed64(BitConverter.DoubleToUInt64Bits(value)); return this; }

    public WasmFunctionBuilder LocalGet(int index) => Indexed(WasmOp.LocalGet, index);
    public WasmFunctionBuilder LocalSet(int index) => Indexed(WasmOp.LocalSet, index);
    public WasmFunctionBuilder LocalTee(int index) => Indexed(WasmOp.LocalTee, index);
    public WasmFunctionBuilder GlobalGet(int index) => Indexed(WasmOp.GlobalGet, index);
    public WasmFunctionBuilder GlobalSet(int index) => Indexed(WasmOp.GlobalSet, index);
    public WasmFunctionBuilder Call(int function) => Indexed(WasmOp.Call, function);
    public WasmFunctionBuilder Br(int depth) => Indexed(WasmOp.Br, depth);
    public WasmFunctionBuilder BrIf(int depth) => Indexed(WasmOp.BrIf, depth);
    public WasmFunctionBuilder RefFunc(int function) => Indexed(WasmOp.RefFunc, function);

    private WasmFunctionBuilder Indexed(byte opcode, int index)
    {
        _code.Byte(opcode);
        _code.U32((uint)index);
        return this;
    }

    public WasmFunctionBuilder CallIndirect(int typeIndex, int table = 0)
    {
        _code.Byte(WasmOp.CallIndirect);
        _code.U32((uint)typeIndex);
        _code.U32((uint)table);
        return this;
    }

    public WasmFunctionBuilder BrTable(IReadOnlyList<int> depths, int fallback)
    {
        _code.Byte(WasmOp.BrTable);
        _code.U32((uint)depths.Count);
        foreach (int d in depths)
            _code.U32((uint)d);
        _code.U32((uint)fallback);
        return this;
    }

    public WasmFunctionBuilder Block(WasmValueType? result = null) => BlockLike(WasmOp.Block, result);
    public WasmFunctionBuilder Loop(WasmValueType? result = null) => BlockLike(WasmOp.Loop, result);
    public WasmFunctionBuilder If(WasmValueType? result = null) => BlockLike(WasmOp.If, result);

    // A block with parameters or several results names a type from the type section.
    public WasmFunctionBuilder BlockOfType(byte opcode, WasmFuncType type)
    {
        _code.Byte(opcode);
        _code.S64(_module.AddType(type));
        return this;
    }

    private WasmFunctionBuilder BlockLike(byte opcode, WasmValueType? result)
    {
        _code.Byte(opcode);
        _code.Byte(result is WasmValueType t ? (byte)t : (byte)0x40);
        return this;
    }

    public WasmFunctionBuilder Else() => Op(WasmOp.Else);
    public WasmFunctionBuilder End() => Op(WasmOp.End);
    public WasmFunctionBuilder Return() => Op(WasmOp.Return);
    public WasmFunctionBuilder Drop() => Op(WasmOp.Drop);
    public WasmFunctionBuilder Select() => Op(WasmOp.Select);

    public WasmFunctionBuilder Memory(byte opcode, uint alignLog2, uint offset)
    {
        _code.Byte(opcode);
        _code.U32(alignLog2);
        _code.U32(offset);
        return this;
    }

    public WasmFunctionBuilder MemorySize() { _code.Byte(WasmOp.MemorySize); _code.Byte(0); return this; }
    public WasmFunctionBuilder MemoryGrow() { _code.Byte(WasmOp.MemoryGrow); _code.Byte(0); return this; }
    public WasmFunctionBuilder MemoryCopy() { Prefixed(WasmPrefixedOp.MemoryCopy); _code.Byte(0); _code.Byte(0); return this; }
    public WasmFunctionBuilder MemoryFill() { Prefixed(WasmPrefixedOp.MemoryFill); _code.Byte(0); return this; }

    public WasmFunctionBuilder MemoryInit(int segment)
    {
        _module.NoteDataIndexUse();
        Prefixed(WasmPrefixedOp.MemoryInit);
        _code.U32((uint)segment);
        _code.Byte(0);
        return this;
    }

    public WasmFunctionBuilder DataDrop(int segment)
    {
        _module.NoteDataIndexUse();
        Prefixed(WasmPrefixedOp.DataDrop);
        _code.U32((uint)segment);
        return this;
    }

    public WasmFunctionBuilder RefNull(WasmValueType type) { _code.Byte(WasmOp.RefNull); _code.Byte((byte)type); return this; }

    // Locals grouped by run, then the code, then the closing end.
    internal byte[] Encode(out int codeStart)
    {
        var w = new WasmByteWriter();
        var groups = new List<(uint Count, WasmValueType Type)>();
        foreach (var local in _locals)
        {
            if (groups.Count > 0 && groups[^1].Type == local)
                groups[^1] = (groups[^1].Count + 1, local);
            else
                groups.Add((1, local));
        }
        w.U32((uint)groups.Count);
        foreach (var (count, type) in groups)
        {
            w.U32(count);
            w.Byte((byte)type);
        }
        codeStart = w.Length;
        w.Bytes(_code.Span);
        w.Byte(WasmOp.End);
        return w.ToArray();
    }
}

public static class WasmSourceMap
{
    public const string SectionName = "lumora.srcmap";

    // The tag of the last mark at or before a byte offset, from the module's source map section.
    public static bool TryFindTag(WasmModule module, int byteOffset, out int tag)
    {
        tag = -1;
        if (byteOffset < 0)
            return false;
        foreach (var section in module.CustomSections)
        {
            if (section.Name != SectionName)
                continue;
            try
            {
                var r = new WasmReader(section.Bytes, 0, section.Bytes.Length);
                uint count = r.ReadU32();
                int best = -1;
                for (uint i = 0; i < count; i++)
                {
                    uint offset = r.ReadU32();
                    uint value = r.ReadU32();
                    if (offset > (uint)byteOffset)
                        break;
                    best = (int)value;
                }
                tag = best;
                return best >= 0;
            }
            catch (WasmFormatException)
            {
                return false;
            }
        }
        return false;
    }
}
