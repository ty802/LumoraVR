// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.Linq;
using Lumora.Script;

namespace Lumora.Shading;

// What a shader graph compiles to. A graph with an error compiles to no source at all, never to half a
// shader, so whoever shows it keeps the last one that worked.
public sealed class ShaderProgram
{
    public string Source { get; }
    public IReadOnlyList<ShaderUniform> Uniforms { get; }
    public IReadOnlyList<CompileDiagnostic> Diagnostics { get; }
    public int TextureSamples { get; }
    public int Loops { get; }

    // The node and output a preview program shows; null for a whole material.
    public string? PreviewNode { get; }
    public string? PreviewPort { get; }

    public bool Succeeded => Source.Length > 0 && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);

    internal ShaderProgram(string source, IReadOnlyList<ShaderUniform> uniforms, IReadOnlyList<CompileDiagnostic> diagnostics,
        int textureSamples, int loops, string? previewNode, string? previewPort)
    {
        Source = source;
        Uniforms = uniforms;
        Diagnostics = diagnostics;
        TextureSamples = textureSamples;
        Loops = loops;
        PreviewNode = previewNode;
        PreviewPort = previewPort;
    }
}
