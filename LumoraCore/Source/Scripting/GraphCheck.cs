// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.Linq;
using Lumora.Script;
using Lumora.Shading;

namespace Lumora.Core.Scripting;

// What checking a graph found: its problems, and what it compiled to, a script or a shader.
public sealed class GraphCheck
{
    public IReadOnlyList<CompileDiagnostic> Diagnostics { get; }
    public CompileResult? Script { get; }
    public ShaderProgram? Shader { get; }

    public IEnumerable<CompileDiagnostic> Errors => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);

    public IEnumerable<CompileDiagnostic> Warnings => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning);

    public GraphCheck(IReadOnlyList<CompileDiagnostic> diagnostics, CompileResult? script, ShaderProgram? shader)
    {
        Diagnostics = diagnostics;
        Script = script;
        Shader = shader;
    }
}
