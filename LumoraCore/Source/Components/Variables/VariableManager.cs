// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Variables;

// Untyped view of one variable identity inside a scope, for tooling that has no T to hand.
public abstract class VariableManager
{
    public readonly string Name;

    // Null once the scope has torn it down.
    public VariableScope? Scope { get; internal set; }

    protected VariableManager(string name, VariableScope scope)
    {
        Name = name;
        Scope = scope;
    }

    public abstract Type ValueType { get; }

    // Drivers included.
    public abstract int ParticipantCount { get; }

    // Zero means the variable does not exist as far as readers are concerned, even when drivers are
    // listening.
    public abstract int ReadableCount { get; }

    // A stale binding checks this and rebinds rather than calling into a dead list.
    public bool IsDisposed { get; protected set; }

    public abstract string ValueToString();

    internal abstract void DetachAll();
}

// Every component registered under one (type, name) identity in one scope, plus the single value
// that identity currently has.
//
// One value, many holders: the first readable variable to register defines the value and every
// later one adopts it, unless that later one asked to override. Writes fan out to ALL of them, so
// two components declaring the same name stay in lockstep instead of one silently shadowing the
// other. Drivers register here too but never contribute a value; they exist to be pushed to. -xlinka
public class VariableManager<T> : VariableManager
{
    private readonly List<IVariableParticipant<T>> _participants = new();
    private readonly List<IVariableParticipant<T>> _broadcastBuffer = new();
    private int _readableCount;
    private T _value;
    private bool _broadcasting;
    private bool _pending;

    // A participant's setter can write back into this same manager (a field binding whose target
    // field another binding is watching, say). Re-entering the fan-out mid-iteration would either
    // skip participants or recurse without end, so a nested write records the new value and the
    // outer loop repeats. The cap turns a genuine write cycle into a dropped update instead of a
    // hung frame. -xlinka
    private const int MaxBroadcastPasses = 8;

    public VariableManager(string name, VariableScope scope) : base(name, scope)
    {
        _value = SyncCoder.GetDefault<T>();
    }

    public override Type ValueType => typeof(T);

    public override int ParticipantCount => _participants.Count;

    public override int ReadableCount => _readableCount;

    // A reference to a destroyed element reads back as nothing rather than handing out a dead target.
    public T Value
    {
        get
        {
            if (_value is IWorldElement { IsDestroyed: true })
                return default!;
            return _value;
        }
    }

    // Reject values this identity cannot hold. Only the type-valued manager uses it.
    public virtual bool IsValidValue(T value) => true;

    public bool HasWritableParticipant()
    {
        for (int i = 0; i < _participants.Count; i++)
        {
            var participant = _participants[i];
            if (!participant.IsWriteOnly && !participant.IsDestroyed && participant.AcceptsWrites)
                return true;
        }
        return false;
    }

    public override string ValueToString()
    {
        var value = Value;
        if (value == null)
            return "<null>";
        if (value is IWorldElement element)
            return element.ParentHierarchyToString();
        return value.ToString() ?? "<null>";
    }

    public void SetValue(T value)
    {
        _value = value;

        if (_broadcasting)
        {
            _pending = true;
            return;
        }

        _broadcasting = true;
        try
        {
            int pass = 0;
            do
            {
                _pending = false;
                Broadcast();
            }
            while (_pending && ++pass < MaxBroadcastPasses);
        }
        finally
        {
            _broadcasting = false;
        }
    }

    private void Broadcast()
    {
        _broadcastBuffer.Clear();
        _broadcastBuffer.AddRange(_participants);
        var value = _value;
        for (int i = 0; i < _broadcastBuffer.Count; i++)
        {
            var participant = _broadcastBuffer[i];
            if (participant.IsDestroyed)
                continue;
            participant.VariableValue = value;
        }
        _broadcastBuffer.Clear();
    }

    internal void Register(IVariableParticipant<T> participant)
    {
        bool hadValue = _readableCount > 0;
        if (!participant.IsWriteOnly)
            _readableCount++;

        if (hadValue && !participant.OverridesOnBind)
        {
            // Somebody already owns this identity's value: adopt it instead of clobbering it.
            participant.VariableValue = Value;
        }
        else if (!participant.IsWriteOnly)
        {
            // First readable one in, or one that insisted on winning: its value becomes the value.
            SetValue(participant.VariableValue);
        }
        else
        {
            // A driver arriving before anything readable gets the type default, which its own
            // fallback then replaces with its configured default.
            participant.VariableValue = SyncCoder.GetDefault<T>();
        }

        _participants.Add(participant);
    }

    internal void Unregister(IVariableParticipant<T> participant)
    {
        if (IsDisposed)
            return;

        bool lostLastValue = false;
        if (!participant.IsWriteOnly)
        {
            _readableCount--;
            if (_readableCount <= 0)
            {
                _readableCount = 0;
                lostLastValue = Scope?.World is { IsDisposed: false };
            }
        }

        _participants.Remove(participant);

        if (_participants.Count == 0)
        {
            IsDisposed = true;
            Scope?.RemoveManager(this);
            Scope = null;
        }
        else if (lostLastValue)
        {
            // Nothing supplies the value any more. Drivers still bound here have to fall back to
            // their own defaults, so push the type default and let each one substitute.
            SetValue(SyncCoder.GetDefault<T>());
        }
    }

    internal override void DetachAll()
    {
        IsDisposed = true;
        for (int i = _participants.Count - 1; i >= 0; i--)
            _participants[i].MarkScopeDirty();
        _participants.Clear();
        _readableCount = 0;
        Scope = null;
    }
}

// A variable holding a Type. Refuses anything that cannot be written down and read back, because
// the value persists and replicates as a type name, not as a runtime handle.
public sealed class TypeVariableManager : VariableManager<Type>
{
    public TypeVariableManager(string name, VariableScope scope) : base(name, scope)
    {
    }

    public override bool IsValidValue(Type value) => value == null || value.FullName != null;
}
