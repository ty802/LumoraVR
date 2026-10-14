// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// The control flow a FlowNode declared. Indexes point into the builder's inputs, outputs and exits:
// Input is the branch or loop condition, the repeat count, the delay, the cooldown or what a For Each
// walks the items of; Exits are the chains in the order the shape names them; Output is the loop index
// or the toggle's state; Item is a For Each's current item, and Source what it walks. Query is the
// inputs a searching For Each hands its host, in order. Reverse is a For's count-down input; Start, End
// and Step are a range loop's inputs. A list For Each walks the list on Input. Enter and Leave name the
// action nodes a scope opens and closes with. Helper names the value node a Change, a Follow or a
// FrameDelta works its answer out with, and PreviousFirst whether that node takes the earlier value first.
// Speed is a Follow's rate input, scaled by SpeedScale, and Stages how many times it eases in a row; Target is
// the field input a Drive holds.
public sealed class FlowShape
{
    public FlowShapeKind Kind { get; }
    public int Input { get; }
    public int[] Exits { get; }
    public int Output { get; }
    public int Item { get; }
    public EachSource Source { get; }
    public int[] Query { get; }
    public int Reverse { get; init; } = -1;
    public int Start { get; init; } = -1;
    public int End { get; init; } = -1;
    public int Step { get; init; } = -1;
    public string? Enter { get; init; }
    public string? Leave { get; init; }
    public string? Helper { get; init; }
    public bool PreviousFirst { get; init; }
    public int Speed { get; init; } = -1;
    public float SpeedScale { get; init; } = 1f;
    public int Stages { get; init; } = 1;
    public int Target { get; init; } = -1;
    // A drive's on input: while it is false the field is let go.
    public int Switch { get; init; } = -1;
    // A holding drive's fields that hear the writes it refuses: the value tried, and how many tries.
    public int Heard { get; init; } = -1;
    public int HeardCount { get; init; } = -1;

    internal FlowShape(FlowShapeKind kind, int input, int[] exits, int output, int item = -1, EachSource source = default,
        int[]? query = null)
    {
        Kind = kind;
        Input = input;
        Exits = exits;
        Output = output;
        Item = item;
        Source = source;
        Query = query ?? Array.Empty<int>();
    }
}
