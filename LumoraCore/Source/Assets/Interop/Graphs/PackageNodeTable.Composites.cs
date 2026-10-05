// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Assets.Interop;

// Package nodes that are one node of ours plus a little around it: a not after a null test, the frame's time
// where the package node reads it unasked, a clamp where it clamps, a guard where ours would stop the call.
// -xlinka
internal static partial class PackageNodeTable
{
    private static void Composites()
    {
        On("NotNull", s =>
        {
            var type = ObjectType(s.Node.Args.Count > 0 ? s.Node.Args[0] : null) ?? "slot";
            string none = s.Add("IsNull", type);
            s.In("Instance", none, "value");
            string there = s.Add("Not", "bool");
            s.Wire(none, "null", there, "value");
            s.Out("", there, "result");
        });

        // The frame's time, which the package node reads for itself.
        Same(s =>
        {
            bool divide = s.Node.Class == "DivDeltaTime";
            var type = s.Arg(0) ?? "float";
            string time = s.Add("FrameDelta");
            string math = s.Add(divide ? "Divide" : "Multiply", $"{type},float");
            s.In("A", math, "a");
            s.Wire(time, "seconds", math, "b");
            s.Out("", math, "result");
        }, "DivDeltaTime", "MulDeltaTime");
        On("InvertedDeltaTime", s =>
        {
            string time = s.Add("FrameDelta");
            string divide = s.Add("Divide", "float,float");
            s.Set(divide, "a", 1d);
            s.Wire(time, "seconds", divide, "b");
            s.Out("", divide, "result");
        });

        // World time in seconds, tenths or tens, as a float.
        Same(s =>
        {
            string time = s.Add("WorldTime");
            string single = s.Add("ToFloat", "double");
            s.Wire(time, "seconds", single, "value");
            double scale = s.Node.Class switch { "WorldTimeTenthFloat" => 0.1, "WorldTime10Float" => 10, _ => 1 };
            if (scale == 1)
            {
                s.Out("", single, "result");
                return;
            }
            string scaled = s.Add("Multiply", "float,float");
            s.Wire(single, "result", scaled, "a");
            s.Set(scaled, "b", scale);
            s.Out("", scaled, "result");
        }, "WorldTimeFloat", "WorldTimeTenthFloat", "WorldTime10Float");

        // The package's lerp holds t between the two ends.
        On("ValueLerp", s =>
        {
            if (s.Arg(0) is not { } type)
            {
                s.Fail("its type has no counterpart here");
                return;
            }
            string lerp = s.Add("Lerp", type == "double" ? "double,double,double" : $"{type},{type},float");
            s.In("From", lerp, "a");
            s.In("To", lerp, "b");
            string held = s.Add("Clamp", type == "double" ? "double,double,double" : "float,float,float");
            s.In("Lerp", held, "value");
            s.Set(held, "min", 0d);
            s.Set(held, "max", 1d);
            s.Wire(held, "result", lerp, "t");
            s.Out("", lerp, "result");
        });

        // The package's repeat wraps into 0..length both ends included.
        On("ValueRepeat", s =>
        {
            if (s.Arg(0) is not { } type)
            {
                s.Fail("its type has no counterpart here");
                return;
            }
            string repeat = s.Add("Repeat", $"{type},{type}");
            s.In("N", repeat, "value");
            string wider = s.Add("Add", $"{type},{type}");
            s.In("Length", wider, "a");
            s.Set(wider, "b", 1d);
            s.Wire(wider, "result", repeat, "length");
            s.Out("", repeat, "result");
        });

        // The package's remainder by zero is 0, where ours would stop the call.
        On("ValueMod", s =>
        {
            if (s.Arg(0) is not { } type)
            {
                s.Fail("its type has no counterpart here");
                return;
            }
            if (type is "float" or "double")
            {
                string plain = s.Add("Remainder", $"{type},{type}");
                s.In("A", plain, "a");
                s.In("B", plain, "b");
                s.Out("", plain, "result");
                return;
            }
            string zero = s.Add("IsEqual", $"{type},{type}");
            s.In("B", zero, "a");
            s.Set(zero, "b", 0d);
            string safe = s.Add("Conditional", type);
            s.Wire(zero, "result", safe, "condition");
            s.Set(safe, "whenTrue", 1d);
            s.In("B", safe, "whenFalse");
            string remainder = s.Add("Remainder", $"{type},{type}");
            s.In("A", remainder, "a");
            s.Wire(safe, "result", remainder, "b");
            string result = s.Add("Conditional", type);
            s.Wire(zero, "result", result, "condition");
            s.Set(result, "whenTrue", 0d);
            s.Wire(remainder, "result", result, "whenFalse");
            s.Out("", result, "result");
        });

        // The sum over the count.
        On("AvgMulti_Float", s =>
        {
            int count = s.Node.Refs("Operands").Count;
            if (count == 0)
            {
                s.OutConstant("", 0d);
                return;
            }
            if (count == 1)
            {
                s.OutThrough("", "Operands");
                return;
            }
            var parts = new string[count];
            System.Array.Fill(parts, "float");
            string sum = s.Add("Add", string.Join(",", parts));
            s.InList("Operands", sum, Lettered);
            string mean = s.Add("Divide", "float,float");
            s.Wire(sum, "result", mean, "a");
            s.Set(mean, "b", (double)count);
            s.Out("", mean, "result");
        });

        // A different entry each time it is read.
        On("PickRandomValue", s =>
        {
            if (s.Arg(0) is not { } type)
            {
                s.Fail("its type has no counterpart here");
                return;
            }
            int count = System.Math.Min(s.Node.Refs("Operands").Count, 4);
            if (count == 0)
            {
                s.OutConstant("", null);
                return;
            }
            string pick = s.Add("RandomInt", "int,int");
            s.Set(pick, "min", 0d);
            s.Set(pick, "max", (double)count);
            string select = s.Add("Select", type);
            s.Wire(pick, "result", select, "index");
            for (int i = 0; i < count; i++)
                s.InAt("Operands", i, select, Lettered(i));
            if (s.Node.Refs("Operands").Count > 4)
                s.Problem("picks from more than four; only the first four are carried");
            s.Out("", select, "result");
        });

        // A loop's start runs ahead of it.
        Same(s =>
        {
            string loop = s.Add("For");
            s.In("Count", loop, "count");
            s.In("Reverse", loop, "reverse");
            if (s.IsRead("Iteration"))
            {
                // Kept, so a turn that waits still reads its own number afterwards.
                string turn = s.Add("StoredValue", "int");
                string keep = s.Add("WriteValue", "int");
                s.Set(keep, "var", turn);
                s.Wire(loop, "index", keep, "value");
                s.Flow(loop, "body", keep);
                s.Exit("LoopIteration", keep, "next");
                s.Out("Iteration", turn, "value");
            }
            else
                s.Exit("LoopIteration", loop, "body");
            s.Exit("LoopEnd", loop, "completed");
            if (!s.Wired("LoopStart"))
            {
                s.Op("", loop);
                return;
            }
            string first = s.Add("Sequence");
            s.Op("", first);
            s.Exit("LoopStart", first, "out0");
            s.Flow(first, "out1", loop);
            if (s.Node.Class == "AsyncFor")
                s.Problem("its turns waited on each other; here they run back to back");
        }, "For", "AsyncFor");

        // A trigger collider is heard entering and leaving; a solid one colliding.
        Same(s =>
        {
            var collider = s.WorldOf("Collider") as Component;
            if (collider == null)
            {
                s.Fail($"listens to {s.Index.Describe(s.Node.Ref("Collider"))}");
                return;
            }
            bool trigger = collider.GetType().GetField("Type")?.GetValue(collider) is ISyncMember { } type
                && (type as IField)?.BoxedValue?.ToString() == "Trigger";
            bool start = s.Node.Class == "OnContactStart";
            string heard = s.Add(trigger ? start ? "OnTriggerEnter" : "OnTriggerExit" : start ? "OnCollisionEnter" : "OnCollisionExit");
            s.Bind(heard, "source", collider.Slot, "slot");
            s.Out("Other", heard, "other");
            s.Exit("OnEvent", heard, "next");
        }, "OnContactStart", "OnContactEnd");
    }
}
