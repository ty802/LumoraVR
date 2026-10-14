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
// scripts, the author's own things for a person's, local things under its own object for a Local script,
// which a world's Local script may also remove from the world as the user at its machine.
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

    // A path split once per text, not on every read: a script's paths are its constants. Bounded, so paths a
    // script builds as it goes cannot grow it forever.
    private sealed class ParsedPath
    {
        public readonly string? ScopeName;
        public readonly string? Name;
        public readonly bool Valid;

        public ParsedPath(string path)
        {
            Variables.ParsePath(path, out ScopeName, out Name);
            Valid = !string.IsNullOrWhiteSpace(Name) && !(path.Contains('/') && ScopeName == null);
        }
    }

    private const int MaxParsedPaths = 4096;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ParsedPath> _paths = new(StringComparer.Ordinal);

    // A path's scope as a script last found it from a slot, kept until any slot moves to another parent or any
    // scope comes, goes or changes name or rule, anywhere. A script reading a variable in a loop walked up the
    // tree and split the path on every read. The last manager read through it rides along, by type.
    private sealed class Found
    {
        public VariableScope Scope = null!;
        public string Name = null!;
        public string? ScopeName;
        public int Hierarchy;
        public int Scopes;
        public VariableManager? Manager;
    }

    private sealed class Lookups
    {
        public const int Max = 1024;
        public readonly Dictionary<(Slot Slot, string Path), Found> Map = new(SameKey.Instance);
    }

    // The slot and the path by reference: a script hands the same string for the same path each call.
    private sealed class SameKey : IEqualityComparer<(Slot Slot, string Path)>
    {
        public static readonly SameKey Instance = new();

        public bool Equals((Slot Slot, string Path) a, (Slot Slot, string Path) b)
            => ReferenceEquals(a.Slot, b.Slot) && ReferenceEquals(a.Path, b.Path);

        public int GetHashCode((Slot Slot, string Path) key)
            => RuntimeHelpers.GetHashCode(key.Slot) * 31 + RuntimeHelpers.GetHashCode(key.Path);
    }

    private static Found? Cached(ScriptContext ctx, Slot slot, string path)
    {
        if (ctx.ScopeLookups is not Lookups lookups || !lookups.Map.TryGetValue((slot, path), out var found))
            return null;
        if (found.Hierarchy != Slot.HierarchyEpoch || found.Scopes != VariableScope.ScopeEpoch || found.Scope.IsDestroyed)
            return null;
        return found;
    }

    private static Found Remember(ScriptContext ctx, Slot slot, string path, VariableScope scope, string name, string? scopeName,
        int hierarchy, int scopes)
    {
        var lookups = ctx.ScopeLookups as Lookups;
        if (lookups == null)
            ctx.ScopeLookups = lookups = new Lookups();
        if (lookups.Map.Count >= Lookups.Max)
            lookups.Map.Clear();
        var found = new Found { Scope = scope, Name = name, ScopeName = scopeName, Hierarchy = hierarchy, Scopes = scopes };
        lookups.Map[(slot, path)] = found;
        return found;
    }

    // The scope a path reaches from slot and the variable's name in it, with the scope part as written.
    public static bool TryFind(ScriptContext ctx, Slot? slot, string? path, out VariableScope scope, out string name, out string? scopeName)
        => TryFind(ctx, slot, path, out scope, out name, out scopeName, out _);

    private static bool TryFind(ScriptContext ctx, Slot? slot, string? path, out VariableScope scope, out string name, out string? scopeName,
        out Found? found)
    {
        scope = null!;
        name = string.Empty;
        scopeName = null;
        found = null;
        if (!ScriptReads.Reached(ctx, slot))
            return false;
        if (string.IsNullOrWhiteSpace(path))
        {
            ctx.Fail("no variable path");
            return false;
        }
        if (Cached(ctx, slot!, path) is { } known)
        {
            scope = known.Scope;
            name = known.Name;
            scopeName = known.ScopeName;
            found = known;
            return true;
        }
        int hierarchy = Slot.HierarchyEpoch;
        int scopes = VariableScope.ScopeEpoch;
        if (!_paths.TryGetValue(path, out var split))
        {
            if (_paths.Count >= MaxParsedPaths)
                _paths.Clear();
            split = _paths.GetOrAdd(path, static text => new ParsedPath(text));
        }
        scopeName = split.ScopeName;
        var parsed = split.Name;
        if (!split.Valid)
        {
            ctx.Fail($"'{path}' is not a variable path");
            return false;
        }
        var reached = slot!.FindScope(scopeName);
        if (reached == null || reached.IsDestroyed)
        {
            ctx.Fail(NoScope(slot, scopeName));
            return false;
        }
        scope = reached;
        name = parsed!;
        found = Remember(ctx, slot!, path, scope, name, scopeName, hierarchy, scopes);
        return true;
    }

    public static bool TryRead<T>(ScriptContext ctx, Slot? slot, string? path, out T value)
    {
        value = default!;
        if (!TryFind(ctx, slot, path, out var scope, out var name, out _, out var found))
            return false;
        VariableManager<T>? manager;
        if (found?.Manager is VariableManager<T> known && !known.IsDisposed && ReferenceEquals(known.Scope, scope))
        {
            manager = known;
        }
        else
        {
            manager = scope.GetManager<T>(name, createIfMissing: false);
            if (found != null)
                found.Manager = manager;
        }
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
        // Every machine runs a world's Local script, so a write that changes nothing is not made.
        if (ctx.WritesAsMachineUser && !RelaysHostOnly(manager) && EqualityComparer<T>.Default.Equals(manager.Value, value))
            return VariableWriteResult.Success;
        var holders = manager.Participants;
        for (int i = 0; i < holders.Count; i++)
        {
            var holder = holders[i];
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
        if (!ScriptReads.Reached(ctx, slot))
            return 0;
        string? wanted = null;
        var trimmed = scopeName?.Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(trimmed))
        {
            wanted = Variables.ProcessName(trimmed);
            if (wanted == null)
            {
                ctx.Fail($"'{trimmed}' is not a scope name");
                return 0;
            }
        }
        var scope = slot!.FindScope(wanted);
        if (scope == null || scope.IsDestroyed)
        {
            ctx.Fail(NoScope(slot, wanted));
            return 0;
        }
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

    private static string NoScope(Slot slot, string? scopeName)
        => string.IsNullOrEmpty(scopeName)
            ? $"no variable scope reaches {ScriptReads.Named(slot)}"
            : $"no scope named '{scopeName}' reaches {ScriptReads.Named(slot)}";

    // Why a variable of that name and type could not take a write, for the call that tried.
    public static string Unwritten(VariableWriteResult result, string name) => result switch
    {
        VariableWriteResult.NotFound => $"no variable '{name}' of that type",
        VariableWriteResult.TypeMismatch => $"the variable '{name}' holds another type",
        _ => $"every holder of '{name}' is driven or read only",
    };

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
            if (holder.IsLocalElement)
                return;
            ScriptWrites.MachineUserReach(ctx, holder);
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

    // Which member each holder type keeps its value in, found once per type.
    private static readonly ConditionalWeakTable<Type, StoreMember> _storeMembers = new();

    private sealed class StoreMember
    {
        public int Index = -1;
        public bool Relays;
    }

    // The member a holder keeps the value in: its own field or reference, or the field it relays.
    private static SyncElement? StoreOf(object holder)
    {
        if (holder is not Component component)
            return null;
        var type = component.GetType();
        if (!_storeMembers.TryGetValue(type, out var known))
        {
            known = FindStore(component);
            _storeMembers.AddOrUpdate(type, known);
        }
        if (known.Index < 0 || known.Index >= component.SyncMemberCount)
            return null;
        var found = component.GetSyncMember(known.Index);
        return known.Relays ? (found as ISyncRef)?.Target as SyncElement : found as SyncElement;
    }

    private static StoreMember FindStore(Component component)
    {
        var known = new StoreMember();
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
            return known;
        for (int i = 0; i < component.SyncMemberCount; i++)
        {
            if (component.GetSyncMemberName(i) != member)
                continue;
            known.Index = i;
            known.Relays = relays;
            break;
        }
        return known;
    }

    // Which members of each holder type are references, found once per type: a value variable has none, and
    // every read used to walk all of its members to find that out.
    private static readonly ConditionalWeakTable<Type, int[]> _referenceMembers = new();

    private static int[] ReferenceMembers(Component component)
    {
        var type = component.GetType();
        if (_referenceMembers.TryGetValue(type, out var known))
            return known;
        var found = new List<int>();
        for (int i = 0; i < component.SyncMemberCount; i++)
        {
            if (component.GetSyncMember(i) is ISyncRef)
                found.Add(i);
        }
        known = found.ToArray();
        _referenceMembers.AddOrUpdate(type, known);
        return known;
    }

    private static bool RelaysHostOnly<T>(VariableManager<T> manager)
    {
        var holders = manager.Participants;
        for (int h = 0; h < holders.Count; h++)
        {
            var holder = holders[h];
            if (holder.IsWriteOnly || holder is not Component component)
                continue;
            foreach (int i in ReferenceMembers(component))
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
