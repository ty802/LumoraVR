// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Components.Variables;

// One participant's half of the link to a scope: which scope it resolved to, which identity it
// registered under, and the last value it saw.
//
// The expensive half of binding is finding the scope, so it only happens when something could have
// changed the answer: the path was edited, a scope above was renamed or destroyed, or the slot
// chain above moved. The last of those is why this watches ParentChanged on EVERY slot from the
// owner up to the root instead of just its own - reparenting a grandparent silently re-homes
// everything below it, and a variable that missed that keeps feeding a scope it no longer sits
// inside. -xlinka
public sealed class VariableBinding<T> : IDisposable
{
    private readonly List<Slot> _watched = new();
    private readonly Action _markDirty;
    private readonly Func<T> _readLocal;

    private Slot? _root;
    private IVariableParticipant<T>? _owner;
    private VariableScope? _scope;
    private VariableManager<T>? _manager;

    private T _lastValue;
    private bool _scopeDirty = true;
    private string? _appliedPath;
    private string? _scopeName;
    private string? _variableName;
    private bool _resolvedOnce;

    public VariableBinding(Slot root, IVariableParticipant<T> owner, Action markDirty, Func<T> readLocal)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _markDirty = markDirty ?? throw new ArgumentNullException(nameof(markDirty));
        _readLocal = readLocal ?? throw new ArgumentNullException(nameof(readLocal));
        _lastValue = Networking.Sync.SyncCoder.GetDefault<T>();
        WatchAncestors();
    }

    // A reference whose target has since been destroyed reads back as nothing.
    public T LastValue
    {
        get
        {
            if (_lastValue is IWorldElement { IsDestroyed: true })
                return default!;
            return _lastValue;
        }
        set => _lastValue = value;
    }

    // Drivers key their default fallback off this.
    public bool HasVariable => (_manager?.ReadableCount ?? 0) > 0;

    public VariableScope? Scope => _scope;

    // The identity this participant is registered under, if any.
    public VariableManager<T>? Manager => _manager;

    // Null when the path carried no prefix.
    public string? BoundScopeName => _scopeName;

    public string? BoundVariableName => _variableName;

    public void MarkScopeDirty()
    {
        if (_owner == null || _owner.IsDestroyed)
            return;
        _scopeDirty = true;
        _markDirty();
    }

    // Bring the registration in line with path (raw "Scope/Name" from the component). Returns true
    // when the registration actually moved, which is the signal a driver needs to re-push its
    // target. hasLocalValue is whether this participant supplies a value of its own that should be
    // pushed into the identity when it differs from what was last seen.
    public bool Refresh(string? path, bool hasLocalValue)
    {
        if (_root == null || _root.IsDestroyed || _owner == null || _owner.IsDestroyed)
            return false;

        bool rebound = false;

        if (!string.Equals(path, _appliedPath, StringComparison.Ordinal) || !_resolvedOnce)
        {
            _appliedPath = path;
            Variables.ParsePath(path, out _scopeName, out _variableName);
            _scopeDirty = true;
        }

        // A manager the scope has torn down is not a valid registration any more, whatever the name
        // still says.
        if (_manager is { IsDisposed: true })
        {
            _manager = null;
            _scopeDirty = true;
        }

        if (_scopeDirty || !_resolvedOnce || (_scope != null && _scope.IsDestroyed))
        {
            _scopeDirty = false;
            _resolvedOnce = true;
            var found = _root.FindScope(_scopeName);
            if (!ReferenceEquals(found, _scope))
            {
                Unbind();
                _scope = found;
            }
        }

        if (!string.Equals(_variableName, _manager?.Name, StringComparison.Ordinal))
        {
            Unbind();
            if (!string.IsNullOrWhiteSpace(_variableName) && _scope != null)
                _manager = _scope.Register(_variableName!, _owner);
            rebound = true;
        }

        // Our own value moved while we were bound: publish it. The LastValue guard is what stops the
        // broadcast we just received from being echoed straight back into the manager.
        if (_manager != null && hasLocalValue)
        {
            var local = _readLocal();
            if (!EqualityComparer<T>.Default.Equals(local, _lastValue))
                _manager.SetValue(local);
        }

        return rebound;
    }

    // Keep watching the hierarchy, so a disabled participant can come back without rebuilding its
    // subscriptions.
    public void Unbind()
    {
        if (_manager != null && _owner != null)
            _manager.Unregister(_owner);
        _manager = null;
        _lastValue = Networking.Sync.SyncCoder.GetDefault<T>();
    }

    public void Dispose()
    {
        UnwatchAncestors();
        Unbind();
        _scope = null;
        _root = null;
        _owner = null;
    }

    // Rebuilt whole rather than patched: after a reparent the chain above the moved slot is entirely
    // different, and working out which ancestors survived costs more than re-walking a handful of
    // parents.
    private void WatchAncestors()
    {
        UnwatchAncestors();
        for (var slot = _root; slot != null; slot = slot.Parent)
        {
            slot.ParentChanged += OnAncestorMoved;
            _watched.Add(slot);
        }
    }

    private void UnwatchAncestors()
    {
        for (int i = 0; i < _watched.Count; i++)
            _watched[i].ParentChanged -= OnAncestorMoved;
        _watched.Clear();
    }

    private void OnAncestorMoved(Slot slot)
    {
        if (_owner == null || _owner.IsDestroyed)
            return;
        WatchAncestors();
        MarkScopeDirty();
    }
}
