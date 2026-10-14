// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Wasm;

internal static class WasmValidator
{
    public static void Validate(WasmModule m, WasmLimits limits)
    {
        foreach (uint typeIndex in m.FunctionTypeIndices)
        {
            if (typeIndex >= (uint)m.Types.Length)
                throw Invalid("unknown type");
        }

        foreach (var table in m.TableTypes)
        {
            if (table.Size.Max is uint max && table.Size.Min > max)
                throw Invalid("size minimum must not be greater than maximum");
        }

        if (m.MemoryTypes.Length > 1)
            throw Invalid("multiple memories");
        foreach (var memory in m.MemoryTypes)
        {
            if (memory.Pages.Min > 65536 || (memory.Pages.Max is uint pagesMax && pagesMax > 65536))
                throw Invalid("memory size must be at most 65536 pages (4GiB)");
            if (memory.Pages.Max is uint max && memory.Pages.Min > max)
                throw Invalid("size minimum must not be greater than maximum");
        }

        // Function references that appear outside of code. A ref.func inside a body may only name one
        // of these, which is what lets an engine know up front which functions can escape as values.
        foreach (var global in m.DefinedGlobals)
        {
            if (global.Init.Kind == WasmConstKind.RefFunc)
                m.DeclaredFunctionReferences.Add(global.Init.Index);
        }
        foreach (var segment in m.Elements)
        {
            foreach (var item in segment.Items)
            {
                if (item.Kind == WasmConstKind.RefFunc)
                    m.DeclaredFunctionReferences.Add(item.Index);
            }
        }
        foreach (var export in m.ExportList)
        {
            if (export.Kind == WasmExternKind.Function)
                m.DeclaredFunctionReferences.Add(export.Index);
        }

        for (int i = 0; i < m.DefinedGlobals.Length; i++)
        {
            var global = m.DefinedGlobals[i];
            global.Init = CheckConstExpr(m, global.Init, global.Type.ValueType);
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var export in m.ExportList)
        {
            if (!names.Add(export.Name))
                throw Invalid("duplicate export name");
            uint count = export.Kind switch
            {
                WasmExternKind.Function => (uint)m.FunctionTypeIndices.Length,
                WasmExternKind.Table => (uint)m.TableTypes.Length,
                WasmExternKind.Memory => (uint)m.MemoryTypes.Length,
                _ => (uint)m.GlobalTypes.Length,
            };
            if (export.Index >= count)
                throw Invalid("unknown " + KindName(export.Kind));
        }

        if (m.Start is uint start)
        {
            if (start >= (uint)m.FunctionTypeIndices.Length)
                throw Invalid("unknown function");
            var type = m.FunctionType(start);
            if (type.Parameters.Length != 0 || type.Results.Length != 0)
                throw Invalid("start function");
        }

        foreach (var segment in m.Elements)
        {
            for (int i = 0; i < segment.Items.Length; i++)
                segment.Items[i] = CheckConstExpr(m, segment.Items[i], segment.ElementType);
            if (segment.Mode == WasmSegmentMode.Active)
            {
                if (segment.Table >= (uint)m.TableTypes.Length)
                    throw Invalid("unknown table");
                segment.Offset = CheckConstExpr(m, segment.Offset, WasmValueType.I32);
                if (m.TableTypes[segment.Table].ElementType != segment.ElementType)
                    throw Invalid("type mismatch");
            }
        }

        foreach (var segment in m.Data)
        {
            if (segment.Mode == WasmSegmentMode.Active)
            {
                if (segment.Memory >= (uint)m.MemoryTypes.Length)
                    throw Invalid("unknown memory");
                segment.Offset = CheckConstExpr(m, segment.Offset, WasmValueType.I32);
            }
        }

        var compiler = new WasmFunctionCompiler(m, limits);
        var compiled = new WasmCompiledFunction[m.Bodies.Length];
        for (int i = 0; i < compiled.Length; i++)
            compiled[i] = compiler.Compile(i);
        m.Compiled = compiled;
    }

    private static WasmConstExpr CheckConstExpr(WasmModule m, WasmConstExpr expr, WasmValueType expected)
    {
        WasmValueType type;
        switch (expr.Kind)
        {
            case WasmConstKind.GlobalGet:
                // Only imported globals are visible to constant expressions, and only immutable ones.
                if (expr.Index >= (uint)m.ImportedGlobalCount)
                    throw Invalid("unknown global");
                var global = m.GlobalTypes[expr.Index];
                if (global.Mutable)
                    throw Invalid("constant expression required");
                type = global.ValueType;
                expr = new WasmConstExpr(WasmConstKind.GlobalGet, type, 0, expr.Index);
                break;
            case WasmConstKind.RefFunc:
                if (expr.Index >= (uint)m.FunctionTypeIndices.Length)
                    throw Invalid("unknown function");
                type = WasmValueType.FuncRef;
                break;
            default:
                type = expr.Type;
                break;
        }
        if (type != expected)
            throw Invalid("type mismatch");
        return expr;
    }

    private static string KindName(WasmExternKind kind) => kind switch
    {
        WasmExternKind.Function => "function",
        WasmExternKind.Table => "table",
        WasmExternKind.Memory => "memory",
        _ => "global",
    };

    internal static WasmValidationException Invalid(string message) => new(message);
}

// Validates one function body and translates it to slot-addressed instructions in the same pass. The
// validation half is the algorithm from the specification's appendix, operand types and control frames
// included; the translation half rides on the heights it already tracks. -xlinka
internal sealed class WasmFunctionCompiler
{
    private const WasmValueType Unknown = 0;
    private const byte FunctionFrame = 0x00;
    private const byte BlockFrame = 0x02;
    private const byte LoopFrame = 0x03;
    private const byte IfFrame = 0x04;

    private struct Frame
    {
        public byte Opcode;
        public WasmValueType[] Start;
        public WasmValueType[] End;
        public int Height;
        public bool Unreachable;
        public int SlotBase;
        public int LoopHead;
        public int FixupHead;
        public bool StartEmitting;
        public bool EndReachable;
        public int ElseFixup;
        public bool HadElse;
    }

    private readonly WasmModule _module;
    private readonly WasmLimits _limits;

    private WasmReader _r = null!;
    private WasmFuncType _type = WasmFuncType.Empty;
    private WasmValueType[] _localTypes = Array.Empty<WasmValueType>();
    private int _localCount;

    private WasmValueType[] _stack = new WasmValueType[64];
    private int _height;
    private int _maxHeight;

    // A local.get copies nothing: the stack entry names the local it came from, and whoever reads the entry
    // reads the local. The copy is made only when that local is about to be written while the entry still
    // stands, or before an instruction that needs the value in its own slot (calls, branches, block ends).
    // A local.set right after the instruction that made its value has that instruction write the local
    // itself. -xlinka
    private int[] _alias = new int[64];
    private int _producer = -1;
    // The furthest instruction anything jumps to: code from there on may be folded together.
    private int _labelAt;

    private Frame[] _frames = new Frame[16];
    private int _depth;

    private readonly List<WasmInstr> _code = new();
    private readonly List<int> _offsets = new();
    private readonly List<int> _branchTable = new();
    private readonly List<int> _moves = new();
    private readonly List<(int Instr, int Next)> _fixups = new();
    private readonly Dictionary<uint, int> _stubs = new();
    private uint[] _labels = new uint[16];
    private bool _emitting;
    private int _fuelIndex;
    private int _opOffset;

    public WasmFunctionCompiler(WasmModule module, WasmLimits limits)
    {
        _module = module;
        _limits = limits;
    }

    public WasmCompiledFunction Compile(int definedIndex)
    {
        var body = _module.Bodies[definedIndex];
        _type = _module.Types[_module.DefinedFunctionTypeIndices[definedIndex]];

        int paramCount = _type.Parameters.Length;
        _localCount = paramCount + body.Locals.Length;
        if (_localCount > _limits.MaxLocalsPerFunction)
            throw WasmValidator.Invalid("too many locals");
        _localTypes = new WasmValueType[_localCount];
        Array.Copy(_type.Parameters, _localTypes, paramCount);
        Array.Copy(body.Locals, 0, _localTypes, paramCount, body.Locals.Length);

        _height = 0;
        _maxHeight = 0;
        _producer = -1;
        _labelAt = 0;
        _depth = 0;
        _code.Clear();
        _offsets.Clear();
        _branchTable.Clear();
        _moves.Clear();
        _fixups.Clear();
        _emitting = true;
        _fuelIndex = -1;

        PushFrame(FunctionFrame, Array.Empty<WasmValueType>(), _type.Results);
        EmitFuel();

        _r = new WasmReader(_module.Bytes, body.Start, body.End);
        _opOffset = body.Start;
        while (_depth > 0)
        {
            if (_r.AtEnd)
                throw WasmReader.Malformed("unexpected end of section or function");
            _opOffset = _r.Position;
            Step(_r.ReadByte());
        }
        if (!_r.AtEnd)
            throw WasmReader.Malformed("section size mismatch");
        CloseFuel();

        return new WasmCompiledFunction
        {
            FunctionIndex = _module.ImportedFunctionCount + definedIndex,
            Offsets = _offsets.ToArray(),
            Type = _type,
            ParamCount = paramCount,
            LocalCount = _localCount,
            FrameSize = _localCount + _maxHeight + 1,
            Code = _code.ToArray(),
            BranchTable = _branchTable.ToArray(),
            Moves = _moves.ToArray(),
        };
    }

    // Operand stack (validation).

    private int Slot(int height) => _localCount + height;

    private void Push(WasmValueType type)
    {
        if (_height == _stack.Length)
        {
            if (_height >= _limits.MaxOperandStack)
                throw WasmValidator.Invalid("operand stack too deep");
            Array.Resize(ref _stack, _stack.Length * 2);
            Array.Resize(ref _alias, _stack.Length);
        }
        _alias[_height] = -1;
        _stack[_height++] = type;
        if (_height > _maxHeight)
            _maxHeight = _height;
    }

    private WasmValueType Pop()
    {
        ref var frame = ref _frames[_depth - 1];
        if (_height == frame.Height)
        {
            if (frame.Unreachable)
                return Unknown;
            throw WasmValidator.Invalid("type mismatch");
        }
        return _stack[--_height];
    }

    private WasmValueType Pop(WasmValueType expected)
    {
        var actual = Pop();
        if (actual != expected && actual != Unknown && expected != Unknown)
            throw WasmValidator.Invalid("type mismatch");
        return actual == Unknown ? expected : actual;
    }

    private void PopTypes(WasmValueType[] types)
    {
        for (int i = types.Length - 1; i >= 0; i--)
            Pop(types[i]);
    }

    private void PushTypes(WasmValueType[] types)
    {
        foreach (var t in types)
            Push(t);
    }

    // br_table puts back exactly what it popped, polymorphic entries included, so that later labels are
    // checked against the real stack and not against the first label's types.
    private void PopTypesKeep(WasmValueType[] types)
    {
        Span<WasmValueType> popped = types.Length <= 32 ? stackalloc WasmValueType[types.Length] : new WasmValueType[types.Length];
        for (int i = types.Length - 1; i >= 0; i--)
        {
            var actual = Pop();
            if (actual != types[i] && actual != Unknown && types[i] != Unknown)
                throw WasmValidator.Invalid("type mismatch");
            popped[i] = actual;
        }
        for (int i = 0; i < popped.Length; i++)
            Push(popped[i]);
    }

    // Where the entry at this height is read from: its local while it is one, else its own slot.
    private int Source(int height) => height >= 0 && _alias[height] >= 0 ? _alias[height] : Slot(height);

    private void Flush(int from)
    {
        for (int i = Math.Max(0, from); i < _height; i++)
        {
            if (_alias[i] < 0)
                continue;
            Emit(WasmIrOp.Copy, Slot(i), _alias[i]);
            _alias[i] = -1;
        }
    }

    private void Materialize(int local)
    {
        for (int i = 0; i < _height; i++)
        {
            if (_alias[i] != local)
                continue;
            Emit(WasmIrOp.Copy, Slot(i), local);
            _alias[i] = -1;
        }
    }

    // The value at source goes into the local: the instruction just emitted writes it there when it made that
    // value, else a copy.
    private void SetLocal(int local, int source)
    {
        if (source == local)
            return;
        if (_emitting && _producer >= 0 && _producer == _code.Count - 1 && _code[_producer].A == source && _labelAt < _code.Count)
        {
            var instr = _code[_producer];
            instr.A = local;
            _code[_producer] = instr;
            _producer = -1;
            return;
        }
        Emit(WasmIrOp.Copy, local, source);
    }

    // The constant the last instruction put in this slot, taken back out of the code so the instruction
    // using it can carry it.
    private bool TakeConstant(int slot, out long value)
    {
        value = 0;
        int last = _code.Count - 1;
        if (!_emitting || _producer != last || last < 0 || _labelAt > last || slot < _localCount)
            return false;
        var instr = _code[last];
        if (instr.Op != WasmIrOp.Const || instr.A != slot)
            return false;
        value = instr.Imm;
        Unemit(1);
        return true;
    }

    private void Unemit(int count)
    {
        _code.RemoveRange(_code.Count - count, count);
        _offsets.RemoveRange(_offsets.Count - count, count);
        _producer = -1;
    }

    private static bool KeepsAliases(byte op)
        => op is 0x01 or 0x10 or 0x11 or 0x1A or (>= 0x20 and <= 0x24) or (>= 0x41 and <= 0xC4) or 0xD0;

    private ref Frame Top => ref _frames[_depth - 1];

    private ref Frame Label(uint depth)
    {
        if (depth >= (uint)_depth)
            throw WasmValidator.Invalid("unknown label");
        return ref _frames[_depth - 1 - (int)depth];
    }

    private static WasmValueType[] LabelTypes(in Frame frame) => frame.Opcode == LoopFrame ? frame.Start : frame.End;

    private void PushFrame(byte opcode, WasmValueType[] start, WasmValueType[] end)
    {
        if (_depth >= _limits.MaxBlockDepth)
            throw WasmValidator.Invalid("nesting too deep");
        if (_depth == _frames.Length)
            Array.Resize(ref _frames, _frames.Length * 2);
        _frames[_depth++] = new Frame
        {
            Opcode = opcode,
            Start = start,
            End = end,
            Height = _height,
            SlotBase = Slot(_height),
            LoopHead = -1,
            FixupHead = -1,
            StartEmitting = _emitting,
            ElseFixup = -1,
        };
        PushTypes(start);
    }

    private void SetUnreachable()
    {
        ref var frame = ref Top;
        _height = frame.Height;
        frame.Unreachable = true;
        _emitting = false;
    }

    // Emission.

    private int Emit(WasmIrOp op, int a = 0, int b = 0, int c = 0, long imm = 0)
    {
        if (!_emitting)
            return -1;
        if (op == WasmIrOp.Copy && Coalesce(a, b))
            return _code.Count - 1;
        _code.Add(new WasmInstr { Op = op, A = a, B = b, C = c, Imm = imm });
        _offsets.Add(_opOffset);
        return _code.Count - 1;
    }

    // A copy straight after another copy joins it, unless something jumps in between them.
    private bool Coalesce(int to, int from)
    {
        int last = _code.Count - 1;
        if (last < 0 || _labelAt > last)
            return false;
        var previous = _code[last];
        if (previous.Op == WasmIrOp.Copy)
        {
            _moves.Add(previous.A);
            _moves.Add(previous.B);
            previous = new WasmInstr { Op = WasmIrOp.Moves, B = _moves.Count - 2, C = 1 };
        }
        else if (previous.Op != WasmIrOp.Moves || previous.B + previous.C * 2 != _moves.Count)
        {
            return false;
        }
        _moves.Add(to);
        _moves.Add(from);
        previous.C++;
        _code[last] = previous;
        return true;
    }

    private int EmitFuel()
    {
        if (!_emitting)
            return -1;
        CloseFuel();
        _fuelIndex = Emit(WasmIrOp.Fuel);
        _labelAt = _fuelIndex;
        return _fuelIndex;
    }

    private void CloseFuel()
    {
        if (_fuelIndex < 0)
            return;
        var instr = _code[_fuelIndex];
        instr.A = _code.Count - _fuelIndex;
        _code[_fuelIndex] = instr;
        _fuelIndex = -1;
    }

    private void EmitCopies(int source, int destination, int count)
    {
        if (!_emitting || source == destination)
            return;
        for (int i = 0; i < count; i++)
            Emit(WasmIrOp.Copy, destination + i, source + i);
    }

    private void AddFixup(ref Frame frame, int instr)
    {
        _fixups.Add((instr, frame.FixupHead));
        frame.FixupHead = _fixups.Count - 1;
        frame.EndReachable = true;
    }

    private void PatchAll(int head, int target)
    {
        for (int i = head; i >= 0; i = _fixups[i].Next)
            Patch(_fixups[i].Instr, target);
    }

    private void Patch(int instr, int target)
    {
        _labelAt = Math.Max(_labelAt, target);
        var ins = _code[instr];
        if (ins.Op == WasmIrOp.Br)
            ins.A = target;
        else
            ins.B = target;
        _code[instr] = ins;
    }

    private void EmitBranchTo(ref Frame target)
    {
        if (!_emitting)
            return;
        if (target.Opcode == LoopFrame)
        {
            Emit(WasmIrOp.Br, target.LoopHead);
            return;
        }
        AddFixup(ref target, Emit(WasmIrOp.Br, -1));
    }

    private void EmitBranchIfTo(int condition, ref Frame target)
    {
        if (!_emitting)
            return;
        if (target.Opcode == LoopFrame)
        {
            Emit(WasmIrOp.BrIf, condition, target.LoopHead);
            return;
        }
        AddFixup(ref target, Emit(WasmIrOp.BrIf, condition, -1));
    }

    // Decoding helpers.

    private (WasmValueType[] Params, WasmValueType[] Results) ReadBlockType()
    {
        byte b = _r.PeekByte();
        if (b == 0x40)
        {
            _r.ReadByte();
            return (Array.Empty<WasmValueType>(), Array.Empty<WasmValueType>());
        }
        if (b is 0x7F or 0x7E or 0x7D or 0x7C or 0x70 or 0x6F or 0x7B)
        {
            var single = _r.ReadValueType();
            return (Array.Empty<WasmValueType>(), new[] { single });
        }
        long index = _r.ReadS33();
        if (index < 0)
            throw WasmReader.Malformed("malformed block type");
        if (index >= _module.Types.Length)
            throw WasmValidator.Invalid("unknown type");
        var type = _module.Types[index];
        return (type.Parameters, type.Results);
    }

    private uint ReadLocalIndex()
    {
        uint index = _r.ReadU32();
        if (index >= (uint)_localCount)
            throw WasmValidator.Invalid("unknown local");
        return index;
    }

    private uint ReadTableIndex()
    {
        uint index = _r.ReadU32();
        if (index >= (uint)_module.TableTypes.Length)
            throw WasmValidator.Invalid("unknown table");
        return index;
    }

    private uint ReadGlobalIndex()
    {
        uint index = _r.ReadU32();
        if (index >= (uint)_module.GlobalTypes.Length)
            throw WasmValidator.Invalid("unknown global");
        return index;
    }

    private void RequireMemory()
    {
        if (_module.MemoryTypes.Length == 0)
            throw WasmValidator.Invalid("unknown memory");
    }

    private void ReadZeroByte()
    {
        if (_r.ReadByte() != 0)
            throw WasmReader.Malformed("zero byte expected");
    }

    private long ReadMemArg(int naturalAlignment)
    {
        uint align = _r.ReadU32();
        // 2^32 and up cannot be an alignment at all; later versions of the format use those bits as flags.
        if (align >= 32)
            throw WasmReader.Malformed("malformed memop flags");
        uint offset = _r.ReadU32();
        RequireMemory();
        if (align > (uint)naturalAlignment)
            throw WasmValidator.Invalid("alignment must not be larger than natural");
        return offset;
    }

    private void RequireDataCount(uint segment)
    {
        if (_module.DataCount is not uint count)
            throw WasmReader.Malformed("data count section required");
        if (segment >= count)
            throw WasmValidator.Invalid("unknown data segment");
    }

    private uint ReadElementIndex()
    {
        uint index = _r.ReadU32();
        if (index >= (uint)_module.Elements.Length)
            throw WasmValidator.Invalid("unknown elem segment");
        return index;
    }

    // One instruction.

    private void Step(byte op)
    {
        if (!KeepsAliases(op))
            Flush(0);
        switch (op)
        {
            case 0x00:
                Emit(WasmIrOp.Unreachable);
                SetUnreachable();
                return;
            case 0x01:
                return;
            case 0x02:
            {
                var (p, r) = ReadBlockType();
                PopTypes(p);
                PushFrame(BlockFrame, p, r);
                return;
            }
            case 0x03:
            {
                var (p, r) = ReadBlockType();
                PopTypes(p);
                PushFrame(LoopFrame, p, r);
                Top.LoopHead = EmitFuel();
                return;
            }
            case 0x04:
            {
                var (p, r) = ReadBlockType();
                int condition = Slot(_height - 1);
                Pop(WasmValueType.I32);
                PopTypes(p);
                int fixup = Emit(WasmIrOp.BrIfZero, condition, -1);
                PushFrame(IfFrame, p, r);
                Top.ElseFixup = fixup;
                return;
            }
            case 0x05:
                StepElse();
                return;
            case 0x0B:
                StepEnd();
                return;
            case 0x0C:
            {
                ref var target = ref Label(_r.ReadU32());
                var types = LabelTypes(in target);
                int source = Slot(_height - types.Length);
                PopTypes(types);
                if (_emitting)
                {
                    EmitCopies(source, target.SlotBase, types.Length);
                    EmitBranchTo(ref target);
                }
                SetUnreachable();
                return;
            }
            case 0x0D:
            {
                ref var target = ref Label(_r.ReadU32());
                int condition = Slot(_height - 1);
                Pop(WasmValueType.I32);
                var types = LabelTypes(in target);
                int source = Slot(_height - types.Length);
                PopTypes(types);
                PushTypes(types);
                if (_emitting)
                {
                    if (types.Length == 0 || source == target.SlotBase)
                    {
                        EmitBranchIfTo(condition, ref target);
                    }
                    else
                    {
                        int skip = Emit(WasmIrOp.BrIfZero, condition, -1);
                        EmitCopies(source, target.SlotBase, types.Length);
                        EmitBranchTo(ref target);
                        Patch(skip, _code.Count);
                    }
                }
                return;
            }
            case 0x0E:
                StepBranchTable();
                return;
            case 0x0F:
            {
                var results = _type.Results;
                int source = Slot(_height - results.Length);
                PopTypes(results);
                if (_emitting)
                {
                    EmitCopies(source, 0, results.Length);
                    Emit(WasmIrOp.Return);
                }
                SetUnreachable();
                return;
            }
            case 0x10:
            {
                uint function = _r.ReadU32();
                if (function >= (uint)_module.FunctionTypeIndices.Length)
                    throw WasmValidator.Invalid("unknown function");
                var type = _module.FunctionType(function);
                Flush(_height - type.Parameters.Length);
                int argumentBase = Slot(_height - type.Parameters.Length);
                PopTypes(type.Parameters);
                PushTypes(type.Results);
                if (function < (uint)_module.ImportedFunctionCount)
                    Emit(WasmIrOp.CallImport, (int)function, argumentBase);
                else
                    Emit(WasmIrOp.Call, (int)(function - (uint)_module.ImportedFunctionCount), argumentBase);
                return;
            }
            case 0x11:
            {
                uint typeIndex = _r.ReadU32();
                uint table = _r.ReadU32();
                if (table >= (uint)_module.TableTypes.Length)
                    throw WasmValidator.Invalid("unknown table");
                if (_module.TableTypes[table].ElementType != WasmValueType.FuncRef)
                    throw WasmValidator.Invalid("type mismatch");
                if (typeIndex >= (uint)_module.Types.Length)
                    throw WasmValidator.Invalid("unknown type");
                var type = _module.Types[typeIndex];
                Flush(_height - type.Parameters.Length - 1);
                int indexSlot = Slot(_height - 1);
                Pop(WasmValueType.I32);
                int argumentBase = Slot(_height - type.Parameters.Length);
                PopTypes(type.Parameters);
                PushTypes(type.Results);
                Emit(WasmIrOp.CallIndirect, (int)table, argumentBase, indexSlot, typeIndex);
                return;
            }
            case 0x1A:
                Pop();
                return;
            case 0x1B:
            {
                int condition = Slot(_height - 1);
                Pop(WasmValueType.I32);
                var t1 = Pop();
                var t2 = Pop();
                if ((t1 != Unknown && !WasmValueTypes.IsNumeric(t1)) || (t2 != Unknown && !WasmValueTypes.IsNumeric(t2)))
                    throw WasmValidator.Invalid("type mismatch");
                if (t1 != t2 && t1 != Unknown && t2 != Unknown)
                    throw WasmValidator.Invalid("type mismatch");
                Push(t1 == Unknown ? t2 : t1);
                int destination = Slot(_height - 1);
                Emit(WasmIrOp.Select, destination, destination + 1, condition);
                return;
            }
            case 0x1C:
            {
                uint count = _r.ReadU32();
                if (count != 1)
                    throw WasmValidator.Invalid("invalid result arity");
                var type = _r.ReadValueType();
                int condition = Slot(_height - 1);
                Pop(WasmValueType.I32);
                Pop(type);
                Pop(type);
                Push(type);
                int destination = Slot(_height - 1);
                Emit(WasmIrOp.Select, destination, destination + 1, condition);
                return;
            }
            case 0x20:
            {
                uint local = ReadLocalIndex();
                Push(_localTypes[local]);
                _alias[_height - 1] = (int)local;
                return;
            }
            case 0x21:
            {
                uint local = ReadLocalIndex();
                int source = Source(_height - 1);
                Pop(_localTypes[local]);
                Materialize((int)local);
                SetLocal((int)local, source);
                return;
            }
            case 0x22:
            {
                uint local = ReadLocalIndex();
                int source = Source(_height - 1);
                Pop(_localTypes[local]);
                Materialize((int)local);
                SetLocal((int)local, source);
                Push(_localTypes[local]);
                _alias[_height - 1] = (int)local;
                return;
            }
            case 0x23:
            {
                uint global = ReadGlobalIndex();
                Push(_module.GlobalTypes[global].ValueType);
                _producer = Emit(WasmIrOp.GlobalGet, Slot(_height - 1), (int)global);
                return;
            }
            case 0x24:
            {
                uint global = ReadGlobalIndex();
                var type = _module.GlobalTypes[global];
                if (!type.Mutable)
                    throw WasmValidator.Invalid("global is immutable");
                int source = Source(_height - 1);
                Pop(type.ValueType);
                if (Counter((int)global, source))
                    return;
                Emit(WasmIrOp.GlobalSet, (int)global, source);
                return;
            }
            case 0x25:
            {
                uint table = ReadTableIndex();
                int slot = Slot(_height - 1);
                Pop(WasmValueType.I32);
                Push(_module.TableTypes[table].ElementType);
                Emit(WasmIrOp.TableGet, slot, (int)table);
                return;
            }
            case 0x26:
            {
                uint table = ReadTableIndex();
                int valueSlot = Slot(_height - 1);
                int indexSlot = Slot(_height - 2);
                Pop(_module.TableTypes[table].ElementType);
                Pop(WasmValueType.I32);
                Emit(WasmIrOp.TableSet, indexSlot, valueSlot, (int)table);
                return;
            }
            case 0x28: Load(WasmIrOp.I32Load, WasmValueType.I32, 2); return;
            case 0x29: Load(WasmIrOp.I64Load, WasmValueType.I64, 3); return;
            case 0x2A: Load(WasmIrOp.I32Load, WasmValueType.F32, 2); return;
            case 0x2B: Load(WasmIrOp.I64Load, WasmValueType.F64, 3); return;
            case 0x2C: Load(WasmIrOp.I32Load8S, WasmValueType.I32, 0); return;
            case 0x2D: Load(WasmIrOp.I32Load8U, WasmValueType.I32, 0); return;
            case 0x2E: Load(WasmIrOp.I32Load16S, WasmValueType.I32, 1); return;
            case 0x2F: Load(WasmIrOp.I32Load16U, WasmValueType.I32, 1); return;
            case 0x30: Load(WasmIrOp.I64Load8S, WasmValueType.I64, 0); return;
            case 0x31: Load(WasmIrOp.I64Load8U, WasmValueType.I64, 0); return;
            case 0x32: Load(WasmIrOp.I64Load16S, WasmValueType.I64, 1); return;
            case 0x33: Load(WasmIrOp.I64Load16U, WasmValueType.I64, 1); return;
            case 0x34: Load(WasmIrOp.I64Load32S, WasmValueType.I64, 2); return;
            case 0x35: Load(WasmIrOp.I64Load32U, WasmValueType.I64, 2); return;
            case 0x36: Store(WasmIrOp.I32Store, WasmValueType.I32, 2); return;
            case 0x37: Store(WasmIrOp.I64Store, WasmValueType.I64, 3); return;
            case 0x38: Store(WasmIrOp.I32Store, WasmValueType.F32, 2); return;
            case 0x39: Store(WasmIrOp.I64Store, WasmValueType.F64, 3); return;
            case 0x3A: Store(WasmIrOp.I32Store8, WasmValueType.I32, 0); return;
            case 0x3B: Store(WasmIrOp.I32Store16, WasmValueType.I32, 1); return;
            case 0x3C: Store(WasmIrOp.I64Store8, WasmValueType.I64, 0); return;
            case 0x3D: Store(WasmIrOp.I64Store16, WasmValueType.I64, 1); return;
            case 0x3E: Store(WasmIrOp.I64Store32, WasmValueType.I64, 2); return;
            case 0x3F:
                ReadZeroByte();
                RequireMemory();
                Push(WasmValueType.I32);
                Emit(WasmIrOp.MemorySize, Slot(_height - 1));
                return;
            case 0x40:
            {
                ReadZeroByte();
                RequireMemory();
                int slot = Slot(_height - 1);
                Pop(WasmValueType.I32);
                Push(WasmValueType.I32);
                Emit(WasmIrOp.MemoryGrow, slot);
                return;
            }
            case 0x41:
                Push(WasmValueType.I32);
                _producer = Emit(WasmIrOp.Const, Slot(_height - 1), imm: (uint)_r.ReadS32());
                return;
            case 0x42:
                Push(WasmValueType.I64);
                _producer = Emit(WasmIrOp.Const, Slot(_height - 1), imm: _r.ReadS64());
                return;
            case 0x43:
                Push(WasmValueType.F32);
                _producer = Emit(WasmIrOp.Const, Slot(_height - 1), imm: _r.ReadFixedU32());
                return;
            case 0x44:
                Push(WasmValueType.F64);
                _producer = Emit(WasmIrOp.Const, Slot(_height - 1), imm: (long)_r.ReadFixedU64());
                return;
            case 0xD0:
                Push(_r.ReadReferenceType());
                Emit(WasmIrOp.Const, Slot(_height - 1));
                return;
            case 0xD1:
            {
                int slot = Slot(_height - 1);
                var type = Pop();
                if (type != Unknown && !WasmValueTypes.IsReference(type))
                    throw WasmValidator.Invalid("type mismatch");
                Push(WasmValueType.I32);
                Emit(WasmIrOp.RefIsNull, slot, slot);
                return;
            }
            case 0xD2:
            {
                uint function = _r.ReadU32();
                if (function >= (uint)_module.FunctionTypeIndices.Length)
                    throw WasmValidator.Invalid("unknown function");
                if (!_module.DeclaredFunctionReferences.Contains(function))
                    throw WasmValidator.Invalid("undeclared function reference");
                Push(WasmValueType.FuncRef);
                Emit(WasmIrOp.RefFunc, Slot(_height - 1), (int)function);
                return;
            }
            case 0xFC:
                StepPrefixed(_r.ReadU32());
                return;
            case 0xFD:
                throw new WasmValidationException("SIMD is not supported");
        }

        if (op >= 0x45 && op <= 0xC4)
        {
            StepNumeric(op);
            return;
        }
        throw WasmReader.Malformed("illegal opcode");
    }

    private void StepElse()
    {
        ref var frame = ref Top;
        if (frame.Opcode != IfFrame || frame.HadElse)
            throw WasmReader.Malformed("unexpected else");
        PopTypes(frame.End);
        if (_height != frame.Height)
            throw WasmValidator.Invalid("type mismatch");
        if (_emitting)
            AddFixup(ref frame, Emit(WasmIrOp.Br, -1));
        frame.HadElse = true;
        frame.Unreachable = false;
        _height = frame.Height;
        _emitting = frame.StartEmitting;
        PushTypes(frame.Start);
        if (_emitting)
        {
            int target = EmitFuel();
            if (frame.ElseFixup >= 0)
                Patch(frame.ElseFixup, target);
            frame.ElseFixup = -1;
        }
    }

    private void StepEnd()
    {
        ref var top = ref Top;
        if (top.Opcode == IfFrame && !top.HadElse && !top.Start.AsSpan().SequenceEqual(top.End))
            throw WasmValidator.Invalid("type mismatch");

        bool fallthrough = _emitting;
        PopTypes(top.End);
        if (_height != top.Height)
            throw WasmValidator.Invalid("type mismatch");

        var frame = top;
        _depth--;
        _height = frame.Height;
        PushTypes(frame.End);

        if (frame.Opcode == FunctionFrame)
        {
            _emitting = fallthrough || frame.EndReachable;
            if (_emitting)
            {
                int target = EmitFuel();
                PatchAll(frame.FixupHead, target);
                EmitCopies(frame.SlotBase, 0, frame.End.Length);
                Emit(WasmIrOp.Return);
            }
            return;
        }

        bool implicitElse = frame.Opcode == IfFrame && !frame.HadElse && frame.StartEmitting;
        _emitting = fallthrough || frame.EndReachable || implicitElse;
        if (_emitting)
        {
            int target = EmitFuel();
            PatchAll(frame.FixupHead, target);
            if (implicitElse && frame.ElseFixup >= 0)
                Patch(frame.ElseFixup, target);
        }
    }

    private void StepBranchTable()
    {
        int count = _r.ReadCount(_limits.MaxBranchTableEntries, "branch table entries");
        if (_labels.Length < count)
            _labels = new uint[Math.Max(count, _labels.Length * 2)];
        for (int i = 0; i < count; i++)
            _labels[i] = _r.ReadU32();
        uint fallback = _r.ReadU32();

        int condition = Slot(_height - 1);
        Pop(WasmValueType.I32);
        ref var fallbackFrame = ref Label(fallback);
        int arity = LabelTypes(in fallbackFrame).Length;
        int source = Slot(_height - arity);
        for (int i = 0; i < count; i++)
        {
            ref var frame = ref Label(_labels[i]);
            var types = LabelTypes(in frame);
            if (types.Length != arity)
                throw WasmValidator.Invalid("type mismatch");
            PopTypesKeep(types);
        }
        PopTypes(LabelTypes(in Label(fallback)));

        if (_emitting)
        {
            int tableOffset = _branchTable.Count;
            for (int i = 0; i <= count; i++)
                _branchTable.Add(-1);
            Emit(WasmIrOp.BrTable, condition, tableOffset, count);
            _stubs.Clear();
            for (int i = 0; i <= count; i++)
            {
                uint label = i < count ? _labels[i] : fallback;
                if (!_stubs.TryGetValue(label, out int stub))
                {
                    stub = _code.Count;
                    _labelAt = stub;
                    ref var frame = ref Label(label);
                    EmitCopies(source, frame.SlotBase, arity);
                    EmitBranchTo(ref frame);
                    _stubs[label] = stub;
                }
                _branchTable[tableOffset + i] = stub;
            }
        }
        SetUnreachable();
    }

    private void Load(WasmIrOp op, WasmValueType type, int naturalAlignment)
    {
        long offset = ReadMemArg(naturalAlignment);
        int slot = Slot(_height - 1);
        Pop(WasmValueType.I32);
        Push(type);
        Emit(op, slot, imm: offset);
    }

    private void Store(WasmIrOp op, WasmValueType type, int naturalAlignment)
    {
        long offset = ReadMemArg(naturalAlignment);
        int valueSlot = Slot(_height - 1);
        int addressSlot = Slot(_height - 2);
        Pop(type);
        Pop(WasmValueType.I32);
        Emit(op, addressSlot, valueSlot, imm: offset);
    }

    private void Unary(WasmIrOp op, WasmValueType input, WasmValueType output)
    {
        int slot = Slot(_height - 1);
        int source = Source(_height - 1);
        Pop(input);
        Push(output);
        _producer = Emit(op, slot, source);
    }

    private void Binary(WasmIrOp op, WasmValueType input, WasmValueType output)
    {
        int right = Source(_height - 1);
        int left = Source(_height - 2);
        int slot = Slot(_height - 2);
        Pop(input);
        Pop(input);
        Push(output);
        var folded = op switch
        {
            WasmIrOp.I32Add => WasmIrOp.I32AddImm,
            WasmIrOp.F32Mul => WasmIrOp.F32MulImm,
            _ => WasmIrOp.Unreachable,
        };
        if (folded != WasmIrOp.Unreachable)
        {
            if (TakeConstant(right, out long value))
            {
                _producer = Emit(folded, slot, left, imm: value);
                return;
            }
            // Either order gives the same sum and the same product, unless both sides are NaN and the
            // payload that wins depends on which came first: a NaN constant stays where it was.
            if (!(op == WasmIrOp.F32Mul && float.IsNaN(BitConverter.UInt32BitsToSingle((uint)PeekConstant(left))))
                && TakeConstant(left, out value))
            {
                _producer = Emit(folded, slot, right, imm: value);
                return;
            }
        }
        _producer = Emit(op, slot, left, right);
    }

    private long PeekConstant(int slot)
    {
        int last = _code.Count - 1;
        return last >= 0 && _code[last].Op == WasmIrOp.Const && _code[last].A == slot ? _code[last].Imm : 0;
    }

    // global.get g, add a constant, global.set g: one step on the global itself.
    private bool Counter(int global, int source)
    {
        int last = _code.Count - 1;
        if (!_emitting || last < 1 || _labelAt > last - 1)
            return false;
        var add = _code[last];
        var get = _code[last - 1];
        if (add.Op != WasmIrOp.I32AddImm || add.A != source || add.B != source
            || get.Op != WasmIrOp.GlobalGet || get.A != source || get.B != global)
            return false;
        Unemit(2);
        Emit(WasmIrOp.GlobalAddI32, global, imm: add.Imm);
        return true;
    }

    // Same bits under another type: a local stays the place to read them.
    private void Reinterpret(WasmValueType input, WasmValueType output)
    {
        int alias = _height > Top.Height ? _alias[_height - 1] : -1;
        Pop(input);
        Push(output);
        _alias[_height - 1] = alias;
    }

    private void StepNumeric(byte op)
    {
        var ir = (WasmIrOp)((int)WasmIrOp.NumericBase + (op - 0x45));
        const WasmValueType I32 = WasmValueType.I32;
        const WasmValueType I64 = WasmValueType.I64;
        const WasmValueType F32 = WasmValueType.F32;
        const WasmValueType F64 = WasmValueType.F64;

        switch (op)
        {
            case 0x45: Unary(ir, I32, I32); return;
            case >= 0x46 and <= 0x4F: Binary(ir, I32, I32); return;
            case 0x50: Unary(ir, I64, I32); return;
            case >= 0x51 and <= 0x5A: Binary(ir, I64, I32); return;
            case >= 0x5B and <= 0x60: Binary(ir, F32, I32); return;
            case >= 0x61 and <= 0x66: Binary(ir, F64, I32); return;
            case >= 0x67 and <= 0x69: Unary(ir, I32, I32); return;
            case >= 0x6A and <= 0x78: Binary(ir, I32, I32); return;
            case >= 0x79 and <= 0x7B: Unary(ir, I64, I64); return;
            case >= 0x7C and <= 0x8A: Binary(ir, I64, I64); return;
            case >= 0x8B and <= 0x91: Unary(ir, F32, F32); return;
            case >= 0x92 and <= 0x98: Binary(ir, F32, F32); return;
            case >= 0x99 and <= 0x9F: Unary(ir, F64, F64); return;
            case >= 0xA0 and <= 0xA6: Binary(ir, F64, F64); return;
            case 0xA7: Unary(ir, I64, I32); return;
            case 0xA8: Unary(ir, F32, I32); return;
            case 0xA9: Unary(ir, F32, I32); return;
            case 0xAA: Unary(ir, F64, I32); return;
            case 0xAB: Unary(ir, F64, I32); return;
            case 0xAC: Unary(ir, I32, I64); return;
            case 0xAD: Unary(ir, I32, I64); return;
            case 0xAE: Unary(ir, F32, I64); return;
            case 0xAF: Unary(ir, F32, I64); return;
            case 0xB0: Unary(ir, F64, I64); return;
            case 0xB1: Unary(ir, F64, I64); return;
            case 0xB2: Unary(ir, I32, F32); return;
            case 0xB3: Unary(ir, I32, F32); return;
            case 0xB4: Unary(ir, I64, F32); return;
            case 0xB5: Unary(ir, I64, F32); return;
            case 0xB6: Unary(ir, F64, F32); return;
            case 0xB7: Unary(ir, I32, F64); return;
            case 0xB8: Unary(ir, I32, F64); return;
            case 0xB9: Unary(ir, I64, F64); return;
            case 0xBA: Unary(ir, I64, F64); return;
            case 0xBB: Unary(ir, F32, F64); return;
            // Reinterpretation changes the type and never the bits, so it emits nothing.
            case 0xBC: Reinterpret(F32, I32); return;
            case 0xBD: Reinterpret(F64, I64); return;
            case 0xBE: Reinterpret(I32, F32); return;
            case 0xBF: Reinterpret(I64, F64); return;
            case 0xC0: Unary(ir, I32, I32); return;
            case 0xC1: Unary(ir, I32, I32); return;
            case >= 0xC2 and <= 0xC4: Unary(ir, I64, I64); return;
        }
        throw WasmReader.Malformed("illegal opcode");
    }

    private void StepPrefixed(uint sub)
    {
        switch (sub)
        {
            case 0: Unary(WasmIrOp.I32TruncSatF32S, WasmValueType.F32, WasmValueType.I32); return;
            case 1: Unary(WasmIrOp.I32TruncSatF32U, WasmValueType.F32, WasmValueType.I32); return;
            case 2: Unary(WasmIrOp.I32TruncSatF64S, WasmValueType.F64, WasmValueType.I32); return;
            case 3: Unary(WasmIrOp.I32TruncSatF64U, WasmValueType.F64, WasmValueType.I32); return;
            case 4: Unary(WasmIrOp.I64TruncSatF32S, WasmValueType.F32, WasmValueType.I64); return;
            case 5: Unary(WasmIrOp.I64TruncSatF32U, WasmValueType.F32, WasmValueType.I64); return;
            case 6: Unary(WasmIrOp.I64TruncSatF64S, WasmValueType.F64, WasmValueType.I64); return;
            case 7: Unary(WasmIrOp.I64TruncSatF64U, WasmValueType.F64, WasmValueType.I64); return;
            case 8:
            {
                uint segment = _r.ReadU32();
                ReadZeroByte();
                RequireDataCount(segment);
                RequireMemory();
                ThreeI32(out int d, out int s, out int n);
                Emit(WasmIrOp.MemoryInit, d, s, n, segment);
                return;
            }
            case 9:
            {
                uint segment = _r.ReadU32();
                RequireDataCount(segment);
                Emit(WasmIrOp.DataDrop, (int)segment);
                return;
            }
            case 10:
            {
                ReadZeroByte();
                ReadZeroByte();
                RequireMemory();
                ThreeI32(out int d, out int s, out int n);
                Emit(WasmIrOp.MemoryCopy, d, s, n);
                return;
            }
            case 11:
            {
                ReadZeroByte();
                RequireMemory();
                ThreeI32(out int d, out int value, out int n);
                Emit(WasmIrOp.MemoryFill, d, value, n);
                return;
            }
            case 12:
            {
                uint element = ReadElementIndex();
                uint table = ReadTableIndex();
                if (_module.Elements[element].ElementType != _module.TableTypes[table].ElementType)
                    throw WasmValidator.Invalid("type mismatch");
                ThreeI32(out int d, out int s, out int n);
                Emit(WasmIrOp.TableInit, d, s, n, ((long)table << 32) | element);
                return;
            }
            case 13:
            {
                uint element = ReadElementIndex();
                Emit(WasmIrOp.ElemDrop, (int)element);
                return;
            }
            case 14:
            {
                uint destination = ReadTableIndex();
                uint source = ReadTableIndex();
                if (_module.TableTypes[destination].ElementType != _module.TableTypes[source].ElementType)
                    throw WasmValidator.Invalid("type mismatch");
                ThreeI32(out int d, out int s, out int n);
                Emit(WasmIrOp.TableCopy, d, s, n, ((long)destination << 32) | source);
                return;
            }
            case 15:
            {
                uint table = ReadTableIndex();
                int countSlot = Slot(_height - 1);
                int initSlot = Slot(_height - 2);
                Pop(WasmValueType.I32);
                Pop(_module.TableTypes[table].ElementType);
                Push(WasmValueType.I32);
                Emit(WasmIrOp.TableGrow, initSlot, countSlot, (int)table);
                return;
            }
            case 16:
            {
                uint table = ReadTableIndex();
                Push(WasmValueType.I32);
                Emit(WasmIrOp.TableSize, Slot(_height - 1), (int)table);
                return;
            }
            case 17:
            {
                uint table = ReadTableIndex();
                int countSlot = Slot(_height - 1);
                int valueSlot = Slot(_height - 2);
                int indexSlot = Slot(_height - 3);
                Pop(WasmValueType.I32);
                Pop(_module.TableTypes[table].ElementType);
                Pop(WasmValueType.I32);
                Emit(WasmIrOp.TableFill, indexSlot, valueSlot, countSlot, table);
                return;
            }
        }
        throw WasmReader.Malformed("illegal opcode");
    }

    private void ThreeI32(out int first, out int second, out int third)
    {
        third = Slot(_height - 1);
        second = Slot(_height - 2);
        first = Slot(_height - 3);
        Pop(WasmValueType.I32);
        Pop(WasmValueType.I32);
        Pop(WasmValueType.I32);
    }
}
