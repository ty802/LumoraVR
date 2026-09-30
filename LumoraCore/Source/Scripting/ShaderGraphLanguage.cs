// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading.Tasks;
using Lumora.Core.Components.Scripting;
using Lumora.Script;
using Lumora.Shading;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Scripting;

// Shader graphs: the shader node registry, wires that convert the way people expect (a number spreads over
// a vector, a color hands a number its brightness), and the shader compiler. The datamodel's types are
// registered before the shader nodes are read, so a texture port is the same "texture" a script's is.
// -xlinka
public sealed class ShaderGraphLanguage : GraphLanguage
{
    public static readonly ShaderGraphLanguage Instance = new();

    private static readonly object _lock = new();
    private static NodeRegistry? _registry;

    private ShaderGraphLanguage()
    {
    }

    public override GraphKind Kind => GraphKind.Shader;

    public override string Noun => "shader graph";

    public override NodeRegistry Registry
    {
        get
        {
            var registry = _registry;
            if (registry != null)
                return registry;
            lock (_lock)
            {
                if (_registry == null)
                {
                    ScriptEnvironment.RegisterTypes();
                    var scanned = ShaderNodes.Registry;
                    foreach (var problem in scanned.Problems)
                        LumoraLogger.Warn($"Shader graph: node left out: {problem}");
                    _registry = scanned;
                }
                return _registry;
            }
        }
    }

    public override bool Accepts(ScriptType wanted, ScriptType given) => ShaderTypes.Accepts(wanted, given);

    public override Task<GraphCheck> CheckAsync(ScriptGraph graph) => Task.Run(() => Check(graph));

    public GraphCheck Check(ScriptGraph graph)
    {
        try
        {
            var program = ShaderGraphCompiler.Compile(graph, Registry);
            return new GraphCheck(program.Diagnostics, null, program);
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"Shader graph: the compiler failed: {ex}");
            return new GraphCheck(new[] { new CompileDiagnostic(DiagnosticSeverity.Error, null, "the shader compiler failed: " + ex.Message) },
                null, null);
        }
    }

    // What secondary on a wire dragged off an input makes: a constant node holding the input's value, and the
    // ports it answers on. Null for a type no constant node carries.
    public static (string Type, string? Signature, string ValuePort, string OutputPort)? ConstantFor(ScriptType type) => type.Name switch
    {
        "float" => ("Value", null, "value", "result"),
        "float2" or "float3" or "float4" => ("Vector", type.Name, "value", "vector"),
        "color" or "colorHDR" => ("RGB", null, "value", "color"),
        _ => null,
    };
}
