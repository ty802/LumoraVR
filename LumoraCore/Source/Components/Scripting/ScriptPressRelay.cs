// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components.Touch;
using Lumora.Core.Math;
using Lumora.Core.Scripting;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Scripting;

// A press on a scripted object's UI button, or on a Call node's card, carried to the authority, the only
// place a Shared script runs. A UI button fires on the presser's own peer, so without this a guest's
// press reaches no script.
//
// It works the way a touch relay does. The relay is minted in the presser's own byte under their user
// root, the one thing a user may always write, and carries a press count and the point pressed. The
// authority reads the presser from the byte, never from a field, caps the rate, and hands each press to
// the target host, which checks the button is still one the presser could press. -xlinka
[ComponentCategory("Interaction/Touch")]
public sealed class ScriptPressRelay : Component
{
    public const int WarnPressesPerSecond = 30;
    private const string RelaySlotName = "ScriptPressRelay";

    public readonly SyncRef<ScriptHost> Target = new();
    public readonly Sync<int> Presses = new();
    public readonly Sync<float3> Point = new();
    public readonly Sync<int> Calls = new();
    public readonly Sync<string> CallNode = new();

    // What a relay on a watched object heard on this machine: the object, and one count per kind.
    public readonly SyncRef<Slot> Source = new();
    public readonly Sync<int> SourcePresses = new();
    public readonly Sync<int> SourceHoverEnters = new();
    public readonly Sync<int> SourceHoverExits = new();
    public readonly Sync<int> SourceClicks = new();

    // Typing into a watched field, a drop onto a watched surface and presses of a watched scripted tool,
    // one count per kind like the rest; the text typed and the thing dropped ride along, the last of them
    // in a frame.
    public readonly Sync<int> SourceEditStarts = new();
    public readonly Sync<int> SourceEditChanges = new();
    public readonly Sync<int> SourceEditEnds = new();
    public readonly Sync<int> SourceEditSubmits = new();
    public readonly Sync<int> SourceReceives = new();
    public readonly Sync<int> SourceToolPrimaryDowns = new();
    public readonly Sync<int> SourceToolPrimaryUps = new();
    public readonly Sync<int> SourceToolSecondaryDowns = new();
    public readonly Sync<int> SourceToolSecondaryUps = new();
    public readonly Sync<string> SourceText = new();
    public readonly SyncRef<Slot> SourceOther = new();

    // A press on a watched panel button landing, held each frame and ending, one count per step like the rest,
    // with where on the button it is riding along.
    public readonly Sync<int> SourcePressBegins = new();
    public readonly Sync<int> SourcePressHolds = new();
    public readonly Sync<int> SourcePressEnds = new();
    public readonly Sync<float2> LocalPoint = new();
    public readonly Sync<float2> NormalizedPoint = new();

    // What a Local script on this machine last sent to its author's Shared scripts on a slot; the count
    // comes last. Messages sent in one frame arrive as the last of them.
    public readonly SyncRef<ScriptHost> MessageSender = new();
    public readonly SyncRef<Slot> MessageTarget = new();
    public readonly Sync<string> MessageText = new();
    public readonly Sync<float> MessageValue = new();
    public readonly Sync<int> Messages = new();

    // A diagnostics panel on this machine that wants the host's script costs. Each bump keeps the host
    // publishing them for ScriptScheduler.CostWatchSeconds.
    public readonly Sync<int> CostWatch = new();

    // Relays are never saved and only their owner writes them, so the authority meets every one at a
    // count of zero; a first press that arrives in the same batch as the relay is still a press.
    private int _seen;
    private int _seenCalls;
    private static readonly int SourceKinds = Enum.GetValues<ScriptSourceKind>().Length;
    private readonly int[] _seenSource = new int[SourceKinds];
    private int _seenMessages;
    private int _seenCostWatch;
    private double _windowStart;
    private int _windowCount;
    private bool _loggedFlood;

    // On the presser's peer: one more press on host, at a world-space point.
    public void Publish(ScriptHost host, in float3 point)
    {
        if (host == null || host.IsDestroyed)
            return;
        if (!ReferenceEquals(Target.Target, host))
            Target.Target = host;
        if (!Point.Value.Equals(point))
            Point.Value = point;
        Presses.Value = unchecked(Presses.Value + 1);
    }

    // On this peer: something a relay heard on a watched object, for the host listening to it.
    public void PublishSource(ScriptHost host, Slot source, ScriptSourceKind kind, in float3 point)
    {
        if (host == null || host.IsDestroyed || source == null || source.IsDestroyed)
            return;
        if (!ReferenceEquals(Target.Target, host))
            Target.Target = host;
        if (!ReferenceEquals(Source.Target, source))
            Source.Target = source;
        if (!Point.Value.Equals(point))
            Point.Value = point;
        var count = SourceCount(kind);
        count.Value = unchecked(count.Value + 1);
    }

    // The same, carrying what was typed or what was dropped. Both are written ahead of the count.
    public void PublishSource(ScriptHost host, Slot source, ScriptSourceKind kind, in float3 point, string? text, Slot? other)
    {
        if (host == null || host.IsDestroyed || source == null || source.IsDestroyed)
            return;
        if ((SourceText.Value ?? string.Empty) != (text ?? string.Empty))
            SourceText.Value = text ?? string.Empty;
        if (!ReferenceEquals(SourceOther.Target, other))
            SourceOther.Target = other!;
        PublishSource(host, source, kind, point);
    }

    // The same for a press step, where on the button it is written ahead of the count.
    public void PublishPress(ScriptHost host, Slot source, ScriptSourceKind kind, in float3 point, in float2 local, in float2 normalized)
    {
        if (host == null || host.IsDestroyed || source == null || source.IsDestroyed)
            return;
        if (!LocalPoint.Value.Equals(local))
            LocalPoint.Value = local;
        if (!NormalizedPoint.Value.Equals(normalized))
            NormalizedPoint.Value = normalized;
        PublishSource(host, source, kind, point);
    }

    // On this peer: a Local script's message for its author's Shared scripts on target.
    public void PublishMessage(ScriptHost sender, Slot target, string text, float value)
    {
        if (sender == null || sender.IsDestroyed || target == null || target.IsDestroyed)
            return;
        if (!ReferenceEquals(MessageSender.Target, sender))
            MessageSender.Target = sender;
        if (!ReferenceEquals(MessageTarget.Target, target))
            MessageTarget.Target = target;
        if (MessageText.Value != text)
            MessageText.Value = text;
        if (MessageValue.Value != value)
            MessageValue.Value = value;
        Messages.Value = unchecked(Messages.Value + 1);
    }

    // On this peer: a panel here is showing the host's script costs.
    public void WatchCosts() => CostWatch.Value = unchecked(CostWatch.Value + 1);

    private Sync<int> SourceCount(ScriptSourceKind kind) => kind switch
    {
        ScriptSourceKind.Press => SourcePresses,
        ScriptSourceKind.HoverEnter => SourceHoverEnters,
        ScriptSourceKind.HoverExit => SourceHoverExits,
        ScriptSourceKind.EditStart => SourceEditStarts,
        ScriptSourceKind.EditChange => SourceEditChanges,
        ScriptSourceKind.EditEnd => SourceEditEnds,
        ScriptSourceKind.EditSubmit => SourceEditSubmits,
        ScriptSourceKind.Received => SourceReceives,
        ScriptSourceKind.ToolPrimaryDown => SourceToolPrimaryDowns,
        ScriptSourceKind.ToolPrimaryUp => SourceToolPrimaryUps,
        ScriptSourceKind.ToolSecondaryDown => SourceToolSecondaryDowns,
        ScriptSourceKind.ToolSecondaryUp => SourceToolSecondaryUps,
        ScriptSourceKind.PressBegin => SourcePressBegins,
        ScriptSourceKind.PressHold => SourcePressHolds,
        ScriptSourceKind.PressEnd => SourcePressEnds,
        _ => SourceClicks,
    };

    // On the presser's peer: one more press on a Call node of host's graph.
    public void PublishCall(ScriptHost host, string nodeId)
    {
        if (host == null || host.IsDestroyed || string.IsNullOrEmpty(nodeId))
            return;
        if (!ReferenceEquals(Target.Target, host))
            Target.Target = host;
        if (CallNode.Value != nodeId)
            CallNode.Value = nodeId;
        Calls.Value = unchecked(Calls.Value + 1);
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (World?.IsAuthority != true)
            return;

        int watch = CostWatch.Value;
        if (watch != _seenCostWatch)
        {
            _seenCostWatch = watch;
            World.Scripts.WatchCosts();
        }

        int presses = Presses.Value;
        int count = unchecked(presses - _seen);
        _seen = presses;
        int calls = Calls.Value;
        int callCount = unchecked(calls - _seenCalls);
        _seenCalls = calls;
        Span<int> sourceCounts = stackalloc int[SourceKinds];
        bool anySource = false;
        for (int kind = 0; kind < SourceKinds; kind++)
        {
            int total = SourceCount((ScriptSourceKind)kind).Value;
            sourceCounts[kind] = unchecked(total - _seenSource[kind]);
            _seenSource[kind] = total;
            anySource |= sourceCounts[kind] > 0;
        }
        int messages = Messages.Value;
        int messageCount = unchecked(messages - _seenMessages);
        _seenMessages = messages;
        if (count <= 0 && callCount <= 0 && !anySource && messageCount <= 0)
            return;

        var presser = TouchUserFilterExtensions.ResolveOwner(this);
        if (presser == null || presser.IsDestroyed)
            return;

        // A message names its own sender and target; everything else is for the host being pressed.
        if (Target.Target is { IsDestroyed: false } host)
        {
            for (int i = 0; i < count && TakeSlot(); i++)
                host.PressFrom(presser, Point.Value);
            for (int i = 0; i < callCount && TakeSlot(); i++)
                host.CallFrom(presser, CallNode.Value ?? string.Empty);
            if (Source.Target is { IsDestroyed: false } source)
            {
                for (int kind = 0; kind < SourceKinds; kind++)
                {
                    var step = (ScriptSourceKind)kind;
                    if (step is ScriptSourceKind.PressBegin or ScriptSourceKind.PressHold or ScriptSourceKind.PressEnd)
                    {
                        // A held press is heard once a frame here, however many of its frames one batch carried,
                        // and is no flood: it is meant to arrive every frame.
                        int steps = step == ScriptSourceKind.PressHold ? System.Math.Min(sourceCounts[kind], 1) : sourceCounts[kind];
                        for (int i = 0; i < steps && (step == ScriptSourceKind.PressHold || TakeSlot()); i++)
                            host.PressStepFrom(presser, source, step, Point.Value, LocalPoint.Value, NormalizedPoint.Value);
                        continue;
                    }
                    for (int i = 0; i < sourceCounts[kind] && TakeSlot(); i++)
                        host.SourceEventFrom(presser, source, step, Point.Value, SourceText.Value, SourceOther.Target);
                }
            }
        }
        if (messageCount > 0 && MessageSender.Target is { IsDestroyed: false } sender && MessageTarget.Target is { IsDestroyed: false } target
            && ScriptPrincipal.ForElement(sender) is { } author && TakeSlot())
            ScriptMessages.ToHost(author, target, ScriptMessages.Text(MessageText.Value), MessageValue.Value, presser);
    }

    // The local user's relay in this world, made on first use.
    internal static ScriptPressRelay? For(World world)
    {
        var root = world.LocalUser?.Root?.Slot;
        if (root == null || root.IsDestroyed)
            return null;

        var existing = root.FindChild(RelaySlotName, false)?.GetComponent<ScriptPressRelay>();
        if (existing != null && !existing.IsDestroyed)
            return existing;
        if (!world.IsLocalAllocationReady)
            return null;

        var slot = world.AddLocalUserSlot(root, RelaySlotName);
        slot.Persistent.Value = false;
        using (world.EnterLocalUserAllocation())
            return slot.AttachComponent<ScriptPressRelay>();
    }

    private bool TakeSlot()
    {
        double now = World?.Time.TotalTime ?? 0.0;
        if (now - _windowStart >= 1.0)
        {
            _windowStart = now;
            _windowCount = 0;
        }
        if (++_windowCount > WarnPressesPerSecond && !_loggedFlood)
        {
            _loggedFlood = true;
            LumoraLogger.Warn($"Script press relay {ParentHierarchyToString()} is pressing faster than {WarnPressesPerSecond}/s.");
        }
        return true;
    }
}
