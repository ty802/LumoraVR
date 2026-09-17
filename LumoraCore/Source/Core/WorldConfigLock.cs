// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Networking.Sync;
using Lumora.Warden;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core;

// The ambient permit for writing the world's two configuration components.
//
// Everything about those components is refused at the datamodel, for everyone, including the host: the
// Session dashboard is the one surface that may author them and it opens this scope around the write.
// Thread-static rather than AsyncLocal because every datamodel write already passes
// HookManager.ThreadCheck, so a write that is not on the world thread is a bug long before it gets
// here - and a thread-static depth costs one static read per BeginModification instead of an
// ExecutionContext lookup. The depth (rather than a bool) is what lets the dash call a component method
// that opens the scope again without the inner using() closing the outer one. -xlinka
public static class WorldConfigEdit
{
    [ThreadStatic]
    private static int _depth;

    public static bool IsOpen => _depth > 0;

    public static IDisposable Enter()
    {
        _depth++;
        return Scope.Instance;
    }

    private sealed class Scope : IDisposable
    {
        internal static readonly Scope Instance = new();

        public void Dispose()
        {
            if (_depth > 0)
                _depth--;
        }
    }
}

// Who the lock applies to, and the questions every enforcement point asks about it.
public static class WorldConfigLock
{
    public static bool IsConfigComponentType(Type? type)
        => type != null && (type == typeof(WorldSettings) || type == typeof(WorldPermissionConfig));

    public static bool IsConfigComponent(object? candidate)
        => candidate is WorldSettings or WorldPermissionConfig;

    // The live pair on the world root. A stray copy somewhere else is NOT this, which is what lets the
    // components destroy themselves when they land on the wrong slot.
    public static bool IsRootConfigComponent(object? candidate)
    {
        if (candidate is not Component component || !IsConfigComponent(component))
            return false;
        var world = component.World;
        var slot = component.Slot;
        return world != null && slot != null && ReferenceEquals(slot, world.RootSlot);
    }

    // Removing, destroying or detaching the root pair is refused outright. Teardown is exempt because a
    // world being disposed takes its root down with it, and an instance the single-instance guard already
    // invalidated is a duplicate that never counted.
    public static bool RefusesRemoval(object? candidate)
    {
        if (!IsRootConfigComponent(candidate))
            return false;
        if (WorldConfigEdit.IsOpen)
            return false;
        var component = (Component)candidate!;
        if (!component.IsValid)
            return false;
        var world = component.World;
        if (world == null || world.IsDestroyed || world.IsDisposed)
            return false;
        return true;
    }

    // A config component that ended up anywhere but the world root came in on a copied item, a pasted
    // graph or a crafted save. It is refused by removing itself rather than by throwing, so a load or a
    // duplicate carrying one still completes with the rest of its content intact.
    public static bool IsMisplaced(Component? component)
    {
        if (component == null || component.IsDestroyed || !IsConfigComponent(component))
            return false;
        var world = component.World;
        if (world == null || world.IsDestroyed || world.RootSlot == null)
            return false;
        return !ReferenceEquals(component.Slot, world.RootSlot);
    }

    // Whether this member (or the member's owning element) belongs to the locked pair. Used by the
    // inspector to render a row read-only and by the undo history to refuse a step.
    public static bool IsLocked(IWorldElement? element)
    {
        if (element is SyncElement { IsConfigLocked: true })
            return true;

        for (int depth = 0; element != null && depth < 8; depth++)
        {
            if (IsConfigComponent(element))
                return true;
            element = (element as Component)?.Slot
                ?? (element as SyncElement)?.Parent
                ?? (element as Worker)?.Parent;
            if (element is Slot)
                return false;
        }
        return false;
    }

    public static bool IsLocked(ISyncMember? member) => IsLocked(member as IWorldElement);
}

// The network half of the same answer, expressed where the gate can see it.
//
// A guest's delta for a config member is already refused by MarkHostOnly, and a guest's attach or
// remove on the root slot is already refused by ownership (world content is minted in the authority
// byte). This rule exists so neither of those is the ONLY thing standing there: it names the two
// components explicitly, so a structural mutation aimed at them from any remote actor is refused on its
// own merits with a reason that says why, rather than on a coincidence of id arithmetic. Local requests
// abstain - the config lock in SyncElement.BeginModification is what answers those. -xlinka
internal sealed class WorldConfigProtectionRule : IDataModelPermissionRule
{
    private readonly World _world;

    public WorldConfigProtectionRule(World world) => _world = world;

    public DataModelPermissionResult Evaluate(
        in DataModelPermissionRequest request, IPermissionActor actor, out string? reason)
    {
        reason = null;

        if (!request.IsNetwork)
            return DataModelPermissionResult.Abstain;

        if (!TouchesConfig(request))
            return DataModelPermissionResult.Abstain;

        reason = "world configuration is host-authored and cannot be changed by a remote peer";
        return DataModelPermissionResult.Deny;
    }

    private bool TouchesConfig(in DataModelPermissionRequest request)
    {
        if (Names(request.Member) || Names(request.Target) || Names(request.Parent))
            return true;

        // A component add/remove arrives as a write on the slot's component collection keyed by the
        // component's own id, so the collection itself says nothing - the key is the whole subject.
        if (request.Key is RefID key && !key.IsNull)
            return WorldConfigLock.IsConfigComponent(_world.ReferenceController?.GetObjectOrNull(key));

        return false;
    }

    private static bool Names(IPermissionTarget? target)
        => target is IWorldElement element && WorldConfigLock.IsLocked(element);
}
