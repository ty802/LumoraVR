// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// A slot the host fills with a world object. The graph only ever names the slot, never a RefID, so
// duplicate, save and load remap it like any other reference.
public sealed class GraphBinding
{
    public string Name { get; }
    public string Type { get; }

    public GraphBinding(string name, string type)
    {
        Name = name;
        Type = type;
    }
}
