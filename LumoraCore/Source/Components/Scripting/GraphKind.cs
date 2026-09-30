// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Scripting;

// What a graph on the node editor is: a Loom script, or a shader graph that makes a material's shader.
// Appended only: saved graphs keep the name.
public enum GraphKind : byte
{
    Script,
    Shader,
}
