// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Lumora.Core.Components.Scripting;
using Lumora.Core.Math;
using Lumora.Script;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Scripting;

// What a world node sees as its first parameter, and the runtime's hand on every node call: each one
// runs inside its principal's scope, so a node body cannot forget to, and the gate judges every write
// as the script's author. -xlinka
public sealed class ScriptContext : ScriptContextBase
{
    public World World { get; }
    public ScriptPrincipal Principal { get; }

    // The slot the script is on, for the node that asks where it is. Null for a script run without a host.
    public Slot? Self { get; }

    internal List<User> UserScratch { get; } = new();

    internal List<Slot> HitScratch { get; } = new();

    internal List<User> PeopleScratch { get; } = new();

    // Rays one script casts in a frame, slots it makes in a second and slots in one copy, past which it is
    // warned. What its author's scripts do between them is counted too: the world's ScriptAuthorLedger.
    public const int WarnRaysPerFrame = 512;
    public const int WarnMadePerSecond = 256;
    public const int WarnCopySlots = 1024;

    // What this script made, for a Local script to take with it when it stops. Swept of what has gone each
    // time it doubles.
    private readonly List<(Slot Slot, int Count)> _made = new();
    private int _sweepMadeAt = 64;
    private double _madeWindow = double.NegativeInfinity;
    private int _madeInWindow;
    private ulong _castFrame = ulong.MaxValue;
    private int _castsInFrame;

    // Local: running on this machine only, for whoever is looking. What that lets it change is in
    // ScriptWrites.
    public bool IsLocal { get; }

    // The host running this script, for the messages it sends. Null for a script run without one.
    internal ScriptHost? Host { get; set; }

    public const int WarnMessagesPerSecond = 60;
    private double _messageWindow = double.NegativeInfinity;
    private int _messagesInWindow;

    // Who set off the call running now: the user its event names, a presser, a grabber, a message's sender.
    // Null when its event names nobody, and for what runs after a Delay. Set as each event is raised.
    public User? Cause { get; internal set; }

    private static readonly ConcurrentDictionary<Type, PropertyInfo?> _causes = new();

    private ScriptUndoRecorder? _undo;

    // What this script's undo nodes record.
    internal ScriptUndoRecorder Undo => _undo ??= new ScriptUndoRecorder(this);

    // The user a payload names, if it names one.
    internal static User? CauseOf<T>(in T payload) where T : struct
        => _causes.GetOrAdd(typeof(T), static type => Array.Find(type.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            static property => property.PropertyType == typeof(User) && property.GetIndexParameters().Length == 0))?.GetValue(payload) as User;

    // Around every call the scheduler makes. A batch the call left open closes as it ends.
    internal void BeginCall()
    {
        Cause = null;
        _undo?.CloseAll();
    }

    internal void EndCall()
    {
        _undo?.CloseAll();
        Cause = null;
    }

    public ScriptContext(World world, ScriptPrincipal principal, Slot? self = null, bool local = false)
    {
        World = world;
        Principal = principal;
        Self = self;
        IsLocal = local;
    }

    public override double Now => World.Time.TotalTime;

    protected override void WriteLog(string text)
        => LumoraLogger.Log($"Loom [{Principal}] {Self?.Name.Value}: {text}");

    protected override void WriteWarning(string text)
        => LumoraLogger.Warn($"Loom [{Principal}] {Self?.Name.Value}: {text}");

    protected override IDisposable? EnterCall(NodeOverload overload)
        => World.DataModelPermissions?.EnterActor(Principal);

    // A destroyed slot, component, field or user reads as nothing to every node.
    protected override bool IsGone(object value)
        => value is IWorldElement { IsDestroyed: true } || value is User { IsDestroyed: true };

    // The groups whose rules exist. Input is the viewer's own, so only a Local script reads it. The rest
    // (affecting users, UI) link for nobody until their quotas and world settings do.
    protected override bool AllowsCapability(Capability capability) => capability switch
    {
        Capability.Core or Capability.WorldRead or Capability.WorldWrite or Capability.WorldStructure
            or Capability.UsersRead or Capability.Physics or Capability.Audio or Capability.Events => true,
        Capability.Input => IsLocal,
        // Links for every script; each request still waits on the sending machine's user for its host.
        Capability.Web => true,
        _ => false,
    };

    internal void CountCast()
    {
        ulong frame = World.Time.UpdateIndex;
        if (frame != _castFrame)
        {
            _castFrame = frame;
            _castsInFrame = 0;
        }
        if (++_castsInFrame == WarnRaysPerFrame + 1)
            Warn(ScriptWarning.Rays, $"more than {WarnRaysPerFrame} rays in one frame");
        if (World.Scripts.Ledger.CountCast(Principal))
            Warn(ScriptWarning.Rays, $"its author's scripts cast more than {ScriptAuthorLedger.WarnRaysPerFrame:N0} rays in one frame");
    }

    // Told before count more slots are made.
    internal void ReserveMade(int count)
    {
        double now = Now;
        if (now - _madeWindow >= 1.0)
        {
            _madeWindow = now;
            _madeInWindow = 0;
        }
        if (_madeInWindow <= WarnMadePerSecond && _madeInWindow + count > WarnMadePerSecond)
            Warn(ScriptWarning.SlotsMade, $"more than {WarnMadePerSecond} slots made in a second");
        if (count > WarnCopySlots)
            Warn(ScriptWarning.Copy, $"one copy of {count:N0} slots");
        if (World.Scripts.Ledger.ReserveMade(Principal, count))
            Warn(ScriptWarning.SlotsMade, $"its author's scripts made more than {ScriptAuthorLedger.WarnMadePerMinute} slots in a minute");
    }

    internal void CountMessage()
    {
        double now = Now;
        if (now - _messageWindow >= 1.0)
        {
            _messageWindow = now;
            _messagesInWindow = 0;
        }
        if (++_messagesInWindow == WarnMessagesPerSecond + 1)
            Warn(ScriptWarning.Messages, $"more than {WarnMessagesPerSecond} messages in a second");
    }

    // A Local script's own things go with it: what it made on this machine is nobody else's to keep.
    internal void DropLocalMade()
    {
        foreach (var (slot, _) in _made)
        {
            if (slot.IsLocalElement && !slot.IsDestroyed)
                slot.Destroy();
        }
        _made.Clear();
    }

    internal void Made(Slot slot, int count)
    {
        _madeInWindow += count;
        if (_made.Count >= _sweepMadeAt)
        {
            _made.RemoveAll(static made => made.Slot.IsDestroyed);
            _sweepMadeAt = System.Math.Max(64, _made.Count * 2);
        }
        _made.Add((slot, count));
        World.Scripts.Ledger.Made(Principal, slot, count);
    }

    // What a For Each walks: the session's users, a slot's children, or what a search found, under the
    // same reads the user and slot nodes make. A slot the script may not see has no children for it.
    protected override int EachCount(EachSource source, object? of) => source switch
    {
        EachSource.Users when AllowsCapability(Capability.UsersRead) => ScriptReads.Users(this).Count,
        EachSource.Children when AllowsCapability(Capability.WorldRead) && of is Slot slot && ScriptReads.Visible(this, slot) => slot.ChildCount,
        EachSource.OverlapSphere or EachSource.OverlapBox when of is Found found => found.Slots.Count,
        _ => 0,
    };

    // A search runs once, as the loop starts, and the loop walks this. It lives as a handle for the call.
    private sealed class Found
    {
        public readonly List<Slot> Slots = new();
    }

    protected override object? EachQuery(EachSource source, ReadOnlySpan<float> query)
    {
        if (!AllowsCapability(Capability.Physics))
            return null;
        var found = new Found();
        switch (source)
        {
            case EachSource.OverlapSphere:
                ScriptPhysics.SearchSphere(this, new float3(query[0], query[1], query[2]), query[3], found.Slots);
                break;
            case EachSource.OverlapBox:
                ScriptPhysics.SearchBox(this, new float3(query[0], query[1], query[2]), new float3(query[3], query[4], query[5]),
                    new floatQ(query[6], query[7], query[8], query[9]), found.Slots);
                break;
            default:
                return null;
        }
        return found;
    }

    protected override object? EachItem(EachSource source, object? of, int index)
    {
        if (index < 0)
            return null;
        switch (source)
        {
            case EachSource.Users when AllowsCapability(Capability.UsersRead):
                var users = ScriptReads.Users(this);
                return index < users.Count ? users[index] : null;
            case EachSource.Children when AllowsCapability(Capability.WorldRead) && of is Slot slot && ScriptReads.Visible(this, slot):
                var children = slot.Children;
                var child = index < children.Count ? children[index] : null;
                return ScriptReads.Visible(this, child) ? child : null;
            case EachSource.OverlapSphere or EachSource.OverlapBox when of is Found found:
                var hit = index < found.Slots.Count ? found.Slots[index] : null;
                return ScriptReads.Visible(this, hit) ? hit : null;
            default:
                return null;
        }
    }
}
