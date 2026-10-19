// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Shading;

// What a node method's parameter says about its port: whether the vertex stage reads it, and the GLSL it
// stands for when nothing feeds it.
internal sealed record ShaderPortInfo(bool VertexStage, GlslDefaultAttribute? Default);
