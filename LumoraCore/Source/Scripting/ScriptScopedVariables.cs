// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Lumora.Core.Components.Variables;
using Lumora.Core.Networking.Sync;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// How scripts reach named variables: through the scope above a slot, by "Scope/Name" or just "Name", the
// same lookup the variable components bind with. A scope part with characters no name may hold names
// nothing, where the components would fall back to the nearest unnamed scope.
//
// A read goes through the scope's value, except where a holder relays a host-only member: that reads as
// nothing, as the member itself would. A write is judged holder by holder before anything moves, since the
// scope fans it out to every holder at once: each must be one the author may write, or none is written.
//
// Making and removing variables is the one place a script attaches or removes a component, so it is kept
// to variable components alone, on a slot the script may already change: world content for the world's
// scripts, the author's own things for a person's, local things under its own object for a Local script.
// Never the world root, a protected slot, or anything in someone's body or hands, and the gate is asked
// as the author before anything is made or taken away. A person's script makes its variables in the
// host's range for that person's scripts, so they are the person's own, and never saved. Nothing is
// refused for count; making a lot is warned about. -xlinka
internal static class ScriptScopedVariables
{
    public const int WarnMadePerSecond = 256;

    private sealed class MadeCount
    {
        public double Window = double.NegativeInfinity;
        public int Count;
    }

    private static readonly ConditionalWeakTable<ScriptContext, MadeCount> _made = new();

    public static bool CanHold(Type type)
        => Variables.IsSupportedValueType(type) || typeof(IWorldElement).IsAssignableFrom(type);

    // The scope a path reaches from slot and the variable's name in it, with the scope part as written.
    public static bool TryFind(ScriptContext ctx, Slot? slot, string? path, out VariableScope scope, out string name, out string? scopeName)
    {
        scope = null!;
        name = string.Empty;
        scopeName = null;
        if (!ScriptReads.Visible(ctx, slot) || string.IsNullOrWhiteSpace(path))
            return false;
        Variables.ParsePath(path, out scopeName, out var parsed);
        if (string.IsNullOrWhiteSpace(parsed) || (path.Contains('/') && scopeName == null))
            return false;
        var found = slot!.FindScope(scopeName);
        if (found == null || found.IsDestroyed)
            return false;
        scope = found;
        name = parsed!;
        return true;
    }

    public static bool TryRead<T>(ScriptContext ctx, Slot? slot, string? path, out T value)
    {
        value = default!;
        if (!TryFind(ctx, slot, path, out var scope, out var name, out _))
            return false;
        var manager = scope.GetManager<T>(name, createIfMissing: false);
        if (manager == null || manager.ReadableCount == 0 || RelaysHostOnly(manager))
            return false;
        var read = manager.Value;
        if (read is IWorldElement element && !ScriptReads.VisibleElement(ctx, element))
            return true;
        value = read;
        return true;
    }

    // Whether a variable of that name and type is there to write, and how the write went when it was.
    public static VariableWriteResult Write<T>(ScriptContext ctx, VariableScope scope, string name, T value)
    {
        var manager = scope.GetManager<T>(name, createIfMissing: false);
        if (manager == null || manager.ReadableCount == 0)
            return scope.TryWrite(name, value);
        value = Admit(ctx, scope.Slot, value);
        foreach (var holder in manager.Participants)
        {
            if (holder.IsWriteOnly || holder.IsDestroyed || !holder.AcceptsWrites)
                continue;
            var store = StoreOf(holder) ?? throw new UnauthorizedAccessException("the variable is held by something scripts do not write");
            ScriptWrites.Check(ctx, store);
        }
        return scope.TryWrite(name, value);
    }

    // Made on the scope's own slot, or on the slot the path was read from. False with exists set when a
    // variable of that name and type is already there.
    public static bool Create<T>(ScriptContext ctx, Slot? slot, string? path, T value, bool onSlot, bool saved, out bool exists)
    {
        exists = false;
        if (!TryFind(ctx, slot, path, out var scope, out var name, out var scopeName))
            return false;
        var manager = scope.GetManager<T>(name, createIfMissing: false);
        if (manager != null && manager.ReadableCount > 0)
        {
            exists = true;
            return false;
        }
        var target = onSlot ? slot! : scope.Slot;
        CheckPlace(ctx, target);
        value = Admit(ctx, target, value);
        CountMade(ctx);

        string full = string.IsNullOrEmpty(scopeName) ? name : scopeName + "/" + name;
        bool persistent = saved && !ctx.IsLocal && ctx.Principal.IsWorldContent;
        try
        {
            using (ctx.IsLocal ? null : ScriptWrites.RangeOf(ctx.World, ctx.Principal))
            {
                if (!Variables.CreateVariable(target, full, value, persistent))
                    throw new InvalidOperationException($"a {typeof(T).Name} cannot be kept in a variable");
            }
        }
        catch (TargetInvocationException wrapped) when (wrapped.InnerException is UnauthorizedAccessException refused)
        {
            throw refused;
        }
        exists = true;
        return true;
    }

    // Every component holding that variable, removed together or not at all.
    public static bool Delete<T>(ScriptContext ctx, Slot? slot, string? path)
    {
        if (!TryFind(ctx, slot, path, out var scope, out var name, out _))
            return false;
        var manager = scope.GetManager<T>(name, createIfMissing: false);
        if (manager == null || manager.ReadableCount == 0)
            return false;
        var holders = new List<Component>();
        manager.CollectHolders(holders);
        return Remove(ctx, holders, new List<VariableManager> { manager }) > 0;
    }

    // Every variable in the scope of that name reached from slot, or only those of one type; an empty name
    // is the nearest scope that takes unnamed variables. Answers how many variables went.
    public static int Clear(ScriptContext ctx, Slot? slot, string? scopeName, Type? only, out bool found)
    {
        found = false;
        if (!ScriptReads.Visible(ctx, slot))
            return 0;
        string? wanted = null;
        var trimmed = scopeName?.Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(trimmed))
        {
            wanted = Variables.ProcessName(trimmed);
            if (wanted == null)
                return 0;
        }
        var scope = slot!.FindScope(wanted);
        if (scope == null || scope.IsDestroyed)
            return 0;
        found = true;

        var managers = new List<VariableManager>();
        var holders = new List<Component>();
        foreach (var manager in scope.LiveVariables)
        {
            if (manager.ReadableCount == 0 || (only != null && manager.ValueType != only))
                continue;
            managers.Add(manager);
            manager.CollectHolders(holders);
        }
        return Remove(ctx, holders, managers);
    }

    // The scopes a path from slot can reach, each once: the nearest that takes unnamed variables and the
    // nearest of each name.
    public static void Reachable(Slot slot, List<VariableScope> into)
    {
        into.Clear();
        var unnamed = slot.FindScope(null);
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var current = slot; current != null; current = current.Parent)
        {
            foreach (var scope in current.GetComponents<VariableScope>())
            {
                if (scope.IsDestroyed)
                    continue;
                bool reached = ReferenceEquals(scope, unnamed);
                if (!string.IsNullOrEmpty(scope.ActiveName) && names.Add(scope.ActiveName!))
                    reached = true;
                if (reached && !into.Contains(scope))
                    into.Add(scope);
            }
        }
    }

    // Removes the holders, every one of them judged first. Answers how many of the variables lost one.
    private static int Remove(ScriptContext ctx, List<Component> holders, List<VariableManager> managers)
    {
        if (holders.Count == 0)
            return 0;
        foreach (var holder in holders)
            CheckRemove(ctx, holder);
        var hit = new HashSet<VariableManager>();
        foreach (var manager in managers)
        {
            var mine = new List<Component>();
            manager.CollectHolders(mine);
            if (mine.Count > 0)
                hit.Add(manager);
        }
        // A destroyed component leaves its scope in the destruction pass at the end of the frame; asked to bind
        // again now, it leaves at once, so the rest of this call reads the variable as gone.
        foreach (var holder in holders)
        {
            if (holder.IsDestroyed)
                continue;
            holder.Destroy();
            if (holder is IVariableParticipant participant)
                participant.RefreshBinding();
        }
        return hit.Count;
    }

    private static void CheckPlace(ScriptContext ctx, Slot target)
    {
        if (ctx.IsLocal)
        {
            if (!target.IsLocalElement || !ScriptReads.Visible(ctx, target))
                throw new UnauthorizedAccessException("a Local script keeps its variables on local things under its own object");
            return;
        }
        if (target.IsLocalElement)
            throw new UnauthorizedAccessException("a Shared script's variables live in the world, not on one machine");
        if (target.IsRootSlot || target.IsProtected)
            throw new UnauthorizedAccessException("scripts do not add variables to the world root or a protected slot");
        if (target.ActiveUserRoot != null)
            throw new UnauthorizedAccessException("scripts do not add variables to anything in someone's body or hands");
        byte owner = target.ReferenceID.GetUserByte();
        if (ctx.Principal.IsWorldContent && owner != RefIDConstants.AUTHORITY_BYTE)
            throw new UnauthorizedAccessException("a world script adds variables to world content only");
        if (!ctx.Principal.IsWorldContent && owner != ctx.Principal.AllocationByte)
            throw new UnauthorizedAccessException("a person's script adds variables to its author's own things only");

        var world = ctx.World;
        var request = new DataModelPermissionRequest(world, null, target, target.Parent, target,
            DataModelPermissionSurface.Component, DataModelPermissionAction.Create, isNetwork: false);
        if (world.DataModelPermissions is { } permissions && !permissions.Authorize(request, out var reason))
            throw new UnauthorizedAccessException(reason ?? "the permission gate refused the new variable");
    }

    private static void CheckRemove(ScriptContext ctx, Component holder)
    {
        if (!ReferenceEquals(holder.World, ctx.World))
            throw new UnauthorizedAccessException("scripts write only their own world");
        var slot = holder.Slot;
        if (ctx.IsLocal)
        {
            if (!holder.IsLocalElement)
                throw new UnauthorizedAccessException("a Local script changes only what it made on this machine");
            return;
        }
        if (slot == null || slot.IsRootSlot || slot.IsProtected)
            throw new UnauthorizedAccessException("scripts do not remove variables from the world root or a protected slot");
        if (slot.ActiveUserRoot != null)
            throw new UnauthorizedAccessException("scripts do not remove variables from anything in someone's body or hands");

        var world = ctx.World;
        var request = new DataModelPermissionRequest(world, null, holder, slot, holder,
            DataModelPermissionSurface.Component, DataModelPermissionAction.Destroy, isNetwork: false);
        if (world.DataModelPermissions is { } permissions && !permissions.Authorize(request, out var reason))
            throw new UnauthorizedAccessException(reason ?? "the permission gate refused the removal");
        if (!slot.MayRemoveComponent(holder))
            throw new UnauthorizedAccessException("the permission gate refused the removal");
    }

    // A slot, user or component a script puts in a variable has to be one it can see, and one in the world
    // when the variable is.
    private static T Admit<T>(ScriptContext ctx, Slot? home, T value)
    {
        if (value is not IWorldElement element)
            return value;
        if (!ScriptReads.VisibleElement(ctx, element))
            return default!;
        if (element.IsLocalElement && home is { IsLocalElement: false })
            throw new UnauthorizedAccessException("a variable in the world cannot point at something on one machine");
        return value;
    }

    // The member a holder keeps the value in: its own field or reference, or the field it relays.
    private static SyncElement? StoreOf(object holder)
    {
        if (holder is not Component component)
            return null;
        string? member = null;
        bool relays = false;
        for (var type = component.GetType(); type != null && member == null; type = type.BaseType)
        {
            if (type == typeof(TypeVariable))
            {
                member = "TypeName";
            }
            else if (type.IsGenericType)
            {
                var open = type.GetGenericTypeDefinition();
                if (open == typeof(ValueVariable<>))
                    member = "Value";
                else if (open == typeof(ReferenceVariable<>))
                    member = "Reference";
                else if (open == typeof(FieldVariable<>) || open == typeof(ReferenceFieldVariable<>))
                    (member, relays) = ("TargetField", true);
            }
        }
        if (member == null)
            return null;
        for (int i = 0; i < component.SyncMemberCount; i++)
        {
            if (component.GetSyncMemberName(i) != member)
                continue;
            var found = component.GetSyncMember(i);
            return relays ? (found as ISyncRef)?.Target as SyncElement : found as SyncElement;
        }
        return null;
    }

    private static bool RelaysHostOnly<T>(VariableManager<T> manager)
    {
        foreach (var holder in manager.Participants)
        {
            if (holder.IsWriteOnly || holder is not Component component)
                continue;
            for (int i = 0; i < component.SyncMemberCount; i++)
            {
                if (component.GetSyncMember(i) is ISyncRef { Target: ConflictingSyncElement { IsHostOnly: true } })
                    return true;
            }
        }
        return false;
    }

    private static void CountMade(ScriptContext ctx)
    {
        var made = _made.GetOrCreateValue(ctx);
        double now = ctx.Now;
        if (now - made.Window >= 1.0)
        {
            made.Window = now;
            made.Count = 0;
        }
        if (++made.Count == WarnMadePerSecond + 1)
            ctx.Warn(ScriptWarning.Variables, $"more than {WarnMadePerSecond} variables made in a second");
    }
}
