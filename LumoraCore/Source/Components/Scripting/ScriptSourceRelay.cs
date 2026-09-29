// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Math;
using Lumora.Core.Scripting;

namespace Lumora.Core.Components.Scripting;

// Sits on an object a script listens to, put there by the authority while it does, never saved. What
// people do to the object with a panel button or a beam happens on their own machines, where only this
// hears it: a press of a panel button on this slot, the local user's beam coming onto the object or
// anything under it and going off again, and a click of that beam on it. Each goes to the listening host,
// directly on the authority and through the user's own press relay anywhere else. -xlinka
[ComponentCategory("Hidden")]
public sealed class ScriptSourceRelay : Component, IButtonPressReceiver
{
    private const double LookSeconds = 1.0;

    public readonly SyncRef<ScriptHost> Host = new();

    private readonly List<(InteractionLaser Laser, Action<IInteractionTarget?> Changed, Action<IInteractionTarget, float3> Activated)> _lasers = new();
    private readonly HashSet<InteractionLaser> _over = new();
    private double _nextLook = double.NegativeInfinity;

    // The text field, drop surface and scripted tool on this slot this relay listens to, and how to stop.
    private readonly List<(Component Part, Action Stop)> _parts = new();

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        double now = World?.Time.TotalTime ?? 0.0;
        if (now < _nextLook)
            return;
        _nextLook = now + LookSeconds;
        FollowLasers();
        FollowParts();
    }

    public override void OnDestroy()
    {
        foreach (var (laser, changed, activated) in _lasers)
        {
            laser.TargetChanged -= changed;
            laser.Activated -= activated;
        }
        _lasers.Clear();
        _over.Clear();
        foreach (var (_, stop) in _parts)
            stop();
        _parts.Clear();
        base.OnDestroy();
    }

    // Typing into a field here, a drop onto a surface here and presses of a scripted tool here all happen on
    // the machine of whoever does them, which is this one only when they are the local user.
    private void FollowParts()
    {
        for (int i = _parts.Count - 1; i >= 0; i--)
        {
            if (!_parts[i].Part.IsDestroyed)
                continue;
            _parts[i].Stop();
            _parts.RemoveAt(i);
        }
        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
            return;
        foreach (var field in slot.GetComponents<TextInput>())
        {
            if (Hooked(field))
                continue;
            Action<TextInput> started = _ => Forward(ScriptSourceKind.EditStart, World?.LocalUser, float3.Zero, string.Empty, null);
            Action<TextInput, string> changed = (_, text) => Forward(ScriptSourceKind.EditChange, World?.LocalUser, float3.Zero, text, null);
            Action<TextInput, string> ended = (_, text) => Forward(ScriptSourceKind.EditEnd, World?.LocalUser, float3.Zero, text, null);
            Action<TextInput, string> submitted = (_, text) => Forward(ScriptSourceKind.EditSubmit, World?.LocalUser, float3.Zero, text, null);
            field.EditingStarted += started;
            field.EditingChanged += changed;
            field.FocusLost += ended;
            field.EditingFinished += submitted;
            var hooked = field;
            _parts.Add((field, () =>
            {
                hooked.EditingStarted -= started;
                hooked.EditingChanged -= changed;
                hooked.FocusLost -= ended;
                hooked.EditingFinished -= submitted;
            }));
        }
        foreach (var surface in slot.GetComponents<DropSurface>())
        {
            if (Hooked(surface))
                continue;
            Action<IGrabbable, Grabber> received = (grabbable, grabber) =>
                Forward(ScriptSourceKind.Received, grabber?.OwningUser ?? World?.LocalUser, float3.Zero, null, (grabbable as Component)?.Slot);
            surface.Received += received;
            var hooked = surface;
            _parts.Add((surface, () => hooked.Received -= received));
        }
        foreach (var tool in slot.GetComponents<ScriptedTool>())
        {
            if (Hooked(tool))
                continue;
            Action<ScriptedTool, bool, bool> pressed = (_, secondary, down) => Forward(
                secondary ? (down ? ScriptSourceKind.ToolSecondaryDown : ScriptSourceKind.ToolSecondaryUp)
                    : (down ? ScriptSourceKind.ToolPrimaryDown : ScriptSourceKind.ToolPrimaryUp),
                World?.LocalUser, float3.Zero, null, null);
            tool.Pressed += pressed;
            var hooked = tool;
            _parts.Add((tool, () => hooked.Pressed -= pressed));
        }
    }

    private bool Hooked(Component part) => _parts.Exists(p => ReferenceEquals(p.Part, part));

    void IButtonPressReceiver.OnButtonPressed(Button button, in UIInteractionContext context)
    {
        // A host on this very slot hears its own buttons.
        if (Host.Target is { } host && ReferenceEquals(host.Slot, Slot))
            return;
        Forward(ScriptSourceKind.Press, context.Actor, context.WorldPoint);
    }

    // The local user's own beams: only they cast on this machine.
    private void FollowLasers()
    {
        for (int i = _lasers.Count - 1; i >= 0; i--)
        {
            var (laser, changed, activated) = _lasers[i];
            if (!laser.IsDestroyed)
                continue;
            _over.Remove(laser);
            _lasers.RemoveAt(i);
        }
        var root = World?.LocalUser?.Root?.Slot;
        if (root == null || root.IsDestroyed)
            return;
        foreach (var laser in root.GetComponentsInChildren<InteractionLaser>())
        {
            if (_lasers.Exists(l => ReferenceEquals(l.Laser, laser)))
                continue;
            var beam = laser;
            Action<IInteractionTarget?> changed = target => Hovered(beam, target);
            Action<IInteractionTarget, float3> activated = (target, point) =>
            {
                if (Covers(target))
                    Forward(ScriptSourceKind.Click, World?.LocalUser, point);
            };
            laser.TargetChanged += changed;
            laser.Activated += activated;
            _lasers.Add((laser, changed, activated));
        }
    }

    private void Hovered(InteractionLaser laser, IInteractionTarget? target)
    {
        bool before = _over.Count > 0;
        if (Covers(target))
            _over.Add(laser);
        else
            _over.Remove(laser);
        bool after = _over.Count > 0;
        if (after != before)
            Forward(after ? ScriptSourceKind.HoverEnter : ScriptSourceKind.HoverExit, World?.LocalUser, float3.Zero);
    }

    private bool Covers(IInteractionTarget? target)
        => target is Component { Slot: { } slot } && !IsDestroyed && (ReferenceEquals(slot, Slot) || slot.IsDescendantOf(Slot));

    private void Forward(ScriptSourceKind kind, User? user, float3 point)
    {
        var world = World;
        if (world == null || Host.Target is not { IsDestroyed: false } host)
            return;
        if (world.IsAuthority)
            host.SourceEventFrom(user ?? world.LocalUser, Slot, kind, point);
        else
            ScriptPressRelay.For(world)?.PublishSource(host, Slot, kind, point);
    }

    private void Forward(ScriptSourceKind kind, User? user, float3 point, string? text, Slot? other)
    {
        var world = World;
        if (world == null || Host.Target is not { IsDestroyed: false } host)
            return;
        if (world.IsAuthority)
            host.SourceEventFrom(user ?? world.LocalUser, Slot, kind, point, text, other);
        else
            ScriptPressRelay.For(world)?.PublishSource(host, Slot, kind, point, text, other);
    }
}
