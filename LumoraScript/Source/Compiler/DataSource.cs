// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Where one input gets its value: another node's output, or a constant.
internal sealed class DataSource
{
    public ResolvedNode? Node { get; }
    public int Output { get; }
    public ConstantValue? Constant { get; }

    private DataSource(ResolvedNode? node, int output, ConstantValue? constant)
    {
        Node = node;
        Output = output;
        Constant = constant;
    }

    public static DataSource Wired(ResolvedNode node, int output) => new(node, output, null);

    public static DataSource Of(ConstantValue constant) => new(null, -1, constant);
}
