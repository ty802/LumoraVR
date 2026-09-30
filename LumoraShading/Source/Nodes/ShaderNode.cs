// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Script;

namespace Lumora.Shading;

// Root of every shader graph node. Run overloads declare the ports the way a Loom node's do, so the
// same editor lays them out; their bodies never run. What a node does is the shader code on [Glsl], or
// an Emit override for a node whose code depends on its settings. -xlinka
public abstract class ShaderNode : ValueNode
{
    // The graph's result. Emitted last, once per stage, and only for its inputs of that stage.
    public virtual bool IsOutput => false;

    public virtual void Emit(ShaderNodeContext context) => context.EmitGlsl();

    // What a Run body says: calling one is a bug, a shader node only ever becomes shader code.
    protected static NotSupportedException GpuOnly => new("a shader node runs on the GPU, never here");
}
