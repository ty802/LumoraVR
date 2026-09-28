// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components;
using Lumora.Core.Math;

namespace Lumora.Core.Scripting;

// A slot's local position, rotation and scale before the script moved it, and after. All three are asked
// of the gate before any is written, so a refused one leaves the slot as it was.
internal sealed class ScriptUndoTransformStep : ScriptUndoStep
{
    private readonly Slot _slot;
    private readonly float3 _position;
    private readonly floatQ _rotation;
    private readonly float3 _scale;
    private float3 _positionAfter;
    private floatQ _rotationAfter;
    private float3 _scaleAfter;

    public ScriptUndoTransformStep(Slot slot)
    {
        _slot = slot;
        _position = slot.LocalPosition.Value;
        _rotation = slot.LocalRotation.Value;
        _scale = slot.LocalScale.Value;
    }

    public override bool Close()
    {
        if (_slot.IsDestroyed)
            return false;
        _positionAfter = _slot.LocalPosition.Value;
        _rotationAfter = _slot.LocalRotation.Value;
        _scaleAfter = _slot.LocalScale.Value;
        return !_positionAfter.Equals(_position) || !_rotationAfter.Equals(_rotation) || !_scaleAfter.Equals(_scale);
    }

    public override bool Undo(ScriptContext ctx) => Put(ctx, _position, _rotation, _scale);

    public override bool Redo(ScriptContext ctx) => Put(ctx, _positionAfter, _rotationAfter, _scaleAfter);

    private bool Put(ScriptContext ctx, in float3 position, in floatQ rotation, in float3 scale)
    {
        if (_slot.IsDestroyed)
            return false;
        ScriptWrites.Check(ctx, _slot.LocalPosition);
        ScriptWrites.Check(ctx, _slot.LocalRotation);
        ScriptWrites.Check(ctx, _slot.LocalScale);
        _slot.LocalPosition.Value = position;
        _slot.LocalRotation.Value = rotation;
        _slot.LocalScale.Value = scale;
        return true;
    }

    public override bool Same(ScriptUndoStep other) => other is ScriptUndoTransformStep step && ReferenceEquals(step._slot, _slot);

    public override string Describe() => UndoLocale.Move.Resolve();
}
