// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Linq;
using System.Threading.Tasks;
using Lumora.Core.Components.Scripting;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Loom scripts: the script registry, exact wires, and the compile cache the host runs from.
public sealed class ScriptGraphLanguage : GraphLanguage
{
    public static readonly ScriptGraphLanguage Instance = new();

    private ScriptGraphLanguage()
    {
    }

    public override GraphKind Kind => GraphKind.Script;

    public override string Noun => "script";

    public override NodeRegistry Registry => ScriptEnvironment.Registry;

    public override bool Accepts(ScriptType wanted, ScriptType given) => ScriptTypes.Accepts(wanted, given);

    public override Task<GraphCheck> CheckAsync(ScriptGraph graph)
        => ScriptEnvironment.CompileAsync(graph).ContinueWith(
            static done => new GraphCheck(done.Result.Diagnostics.ToArray(), done.Result, null),
            TaskContinuationOptions.ExecuteSynchronously);
}
