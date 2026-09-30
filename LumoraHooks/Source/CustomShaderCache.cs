// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Godot;

namespace Lumora.Godot.Hooks;

// One compiled Godot shader per custom source text, shared by every material showing it. Fifty copies of
// an imported orb are one compile, and a shader graph's node previews share whatever they have in common
// with each other and with the material. The most recently used are kept; a shader dropped from here stays
// alive for as long as a material still wears it. Main thread only, like every hook. -xlinka
internal static class CustomShaderCache
{
    private const int Capacity = 128;

    private static readonly Dictionary<string, LinkedListNode<(string Source, Shader Shader)>> _bySource = new(StringComparer.Ordinal);
    private static readonly LinkedList<(string Source, Shader Shader)> _recent = new();

    public static Shader Get(string source)
    {
        if (_bySource.TryGetValue(source, out var known))
        {
            _recent.Remove(known);
            if (GodotObject.IsInstanceValid(known.Value.Shader))
            {
                _recent.AddFirst(known);
                return known.Value.Shader;
            }
            _bySource.Remove(source);
        }

        var shader = new Shader { Code = source };
        _bySource[source] = _recent.AddFirst((source, shader));
        while (_recent.Count > Capacity)
        {
            var oldest = _recent.Last!;
            _recent.RemoveLast();
            _bySource.Remove(oldest.Value.Source);
        }
        return shader;
    }
}
