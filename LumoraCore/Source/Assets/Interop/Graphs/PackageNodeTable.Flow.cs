// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lumora.Core.Assets.Interop;

// Flow: branches, waits, starting a chain beside another, sending flow to receivers by tag
// (PackageNodeTable.Impulses.cs), and the buttons on a card. A wait here ends its chain and runs what follows as a call of its own, so a chain that went on at
// once beside a wait (a started task, a wait's own trigger) is a Sequence that runs it first. -xlinka
internal static partial class PackageNodeTable
{
    private static void Flow()
    {
        Map("If", "If", null, ins: "Condition=condition", ops: "=in", exits: "OnTrue=true OnFalse=false");

        On("StartAsyncTask", s =>
        {
            bool task = s.Wired("TaskStart"), started = s.Wired("OnStarted");
            if (task && started)
            {
                string both = s.Add("Sequence");
                s.Op("", both);
                s.Exit("TaskStart", both, "out0");
                s.Exit("OnStarted", both, "out1");
                return;
            }
            s.OpThrough("", task ? "TaskStart" : started ? "OnStarted" : "OnFailed");
        });

        Same(s =>
        {
            string wait = s.Add("Delay");
            s.In("Duration", wait, "seconds", 0d);
            Start(s, wait);
            s.Exit("Next", wait, "then");
        }, "DelaySecondsFloat", "DelayUpdatesOrSecondsFloat");

        // A number of frames: one Delay of no time waits one frame, so that many in a row.
        Same(s =>
        {
            int frames = 1;
            if (s.Feeder("Updates") is { } count && count.Class == "ValueInput" && count.Value("Value") is IConvertible n)
                frames = System.Math.Clamp(Convert.ToInt32(n, CultureInfo.InvariantCulture), 1, 64);
            else if (s.Wired("Updates"))
                s.Problem("waits a frame count worked out as it runs; it waits one frame");
            string first = s.Add("Delay");
            s.Set(first, "seconds", 0d);
            string last = first;
            for (int i = 1; i < frames; i++)
            {
                string next = s.Add("Delay");
                s.Set(next, "seconds", 0d);
                s.Flow(last, "then", next);
                last = next;
            }
            if (s.Node.Class == "DelayUpdatesWithObject")
            {
                if (ObjectType(s.Node.Args.Count > 0 ? s.Node.Args[0] : null) is not { } type)
                {
                    s.Fail("its value's type has no counterpart here");
                    return;
                }
                string kept = s.Add("StoredValue", type);
                string keep = s.Add("WriteValue", type);
                s.Set(keep, "var", kept);
                s.In("Value", keep, "value");
                s.Out("DelayedValue", kept, "value");
                s.Op("", keep);
                s.Flow(keep, "next", first);
                Start(s, null);
            }
            else
            {
                Start(s, first);
            }
            s.Exit("Next", last, "then");
        }, "DelayUpdates", "DelayUpdatesWithObject");

        On("LocalImpulseTimeoutSeconds", s =>
        {
            string gate = s.Add("Cooldown");
            s.In("Timeout", gate, "seconds", 0d);
            s.Op("Trigger", gate);
            s.Exit("Next", gate, "next");
        });

        Same(s =>
        {
            string call = s.Add("Call");
            s.Exit("Target", call, "next");
        }, "CallInput", "AsyncCallInput");

        Same(SendImpulse, "DynamicImpulseTrigger", "DynamicImpulseTriggerWithObject");
        Same(ReceiveImpulse, "DynamicImpulseReceiver", "DynamicImpulseReceiverWithObject");
    }

    // A wait with a trigger of its own: the trigger's chain runs first, then the wait. Wait null means the
    // node's own flow in is already taken and only the trigger needs placing ahead of it.
    private static void Start(PackageNodeScope s, string? wait)
    {
        if (!s.Wired("OnTriggered"))
        {
            if (wait != null)
                s.Op("", wait);
            return;
        }
        string both = s.Add("Sequence");
        s.Op("", both);
        s.Exit("OnTriggered", both, "out0");
        if (wait != null)
            s.Flow(both, "out1", wait);
    }
}
