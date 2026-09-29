// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components.Magnets;
using Lumora.Core.Math;
using Lumora.Core.Scripting;

namespace Lumora.Core.Components.Scripting;

// Pulls a dragged wire's loose end onto the nearest port it fits. Made when the drag starts: a socket on
// every fitting port, each taking only this drag's own magnet, and that magnet riding the loose end, so
// the magnet's own search says which socket the end is within reach of and no other magnet in the world
// ever takes a port. All on local slots, gone with the drag.
//
// The ports may be in other groups, which the wire would join, and each is found where its own graph draws
// it. A hand pulls a wire across arm's length, so only ports within SearchRadius of where the drag began
// get a socket: a world full of groups would otherwise set out hundreds. -xlinka
public sealed class ScriptPortSnap : IDisposable
{
    private const float SearchRadius = 2.5f;

    private readonly Slot _end;
    private readonly Magnet _magnet;
    private readonly Slot _sockets;
    private readonly Dictionary<MagnetSocket, (ScriptGraphNode Node, ScriptCardHandleKind Kind, string Port)> _ports = new();
    private readonly List<MagnetSocket> _scratch = new();

    // The port the end is pulled onto as of the last Pull, if any.
    public (ScriptGraphNode Node, ScriptCardHandleKind Kind, string Port)? Snapped { get; private set; }

    public int SocketCount => _ports.Count;

    private ScriptPortSnap(Slot end, Magnet magnet, Slot sockets)
    {
        _end = end;
        _magnet = magnet;
        _sockets = sockets;
    }

    // Null when nothing in reach fits the port the drag began at.
    public static ScriptPortSnap? Begin(ScriptCardHandle from, float reach)
    {
        var world = from.World;
        if (world == null || from.Node is not { } source || source.Space is not { View: { } view }
            || !view.TryPortPoint(source, from.Kind, from.Port, out var origin))
            return null;
        var fitting = ScriptGraphEditing.FittingPorts(from);
        if (fitting.Count == 0)
            return null;

        var end = world.RootSlot.AddLocalSlot("Wire end");
        var magnet = end.AttachComponent<Magnet>();
        magnet.Radius.Value = 0f;
        magnet.UseGuides.Value = false;
        magnet.AllowAutoAttach.Value = false;
        var sockets = world.RootSlot.AddLocalSlot("Port sockets");
        var snap = new ScriptPortSnap(end, magnet, sockets);
        foreach (var port in fitting)
        {
            if (!TryPortPoint(port, out var point) || (point - origin).Length > SearchRadius)
                continue;
            var slot = sockets.AddLocalSlot(port.Port);
            slot.GlobalPosition = point;
            var socket = slot.AttachComponent<MagnetSocket>();
            socket.MaxDistance.Value = reach;
            socket.SettleTime.Value = 0f;
            socket.MagnetWhitelist.Add(magnet);
            magnet.SocketWhitelist.Add(socket);
            snap._ports[socket] = port;
        }
        return snap;
    }

    // Where a loose end at this point in the world is pulled to: the fitting port nearest it within
    // reach, or null for none.
    public float3? Pull(float3 loose)
    {
        Snapped = null;
        if (_end.IsDestroyed)
            return null;
        foreach (var (socket, port) in _ports)
        {
            if (!socket.IsDestroyed && TryPortPoint(port, out var point))
                socket.Slot.GlobalPosition = point;
        }
        _end.GlobalPosition = loose;

        MagnetSocket? best = null;
        float nearest = float.MaxValue;
        if (MagnetHelper.CollectSockets(_magnet, loose, floatQ.Identity, _scratch) > 0)
        {
            foreach (var socket in _scratch)
            {
                float distance = socket.DistanceTo(loose);
                if (distance < nearest)
                {
                    nearest = distance;
                    best = socket;
                }
            }
        }
        if (best == null || !_ports.TryGetValue(best, out var found) || found.Node.IsDestroyed)
            return null;
        Snapped = found;
        return best.Slot.GlobalPosition;
    }

    // The card handle for the port the end is pulled onto.
    public ScriptCardHandle? SnappedHandle()
        => Snapped is { } port && !port.Node.IsDestroyed ? port.Node.Space?.View?.CardOf(port.Node)?.FindHandle(port.Kind, port.Port) : null;

    public void Dispose()
    {
        Snapped = null;
        _ports.Clear();
        _scratch.Clear();
        if (!_sockets.IsDestroyed)
            _sockets.Destroy();
        if (!_end.IsDestroyed)
            _end.Destroy();
    }

    // Where a port meets its wires, as its own graph draws it.
    private static bool TryPortPoint((ScriptGraphNode Node, ScriptCardHandleKind Kind, string Port) port, out float3 point)
    {
        point = default;
        return !port.Node.IsDestroyed && port.Node.Space?.View is { } view && view.TryPortPoint(port.Node, port.Kind, port.Port, out point);
    }
}
