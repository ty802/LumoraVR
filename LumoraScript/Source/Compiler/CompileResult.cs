// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Lumora.Wasm;

namespace Lumora.Script;

// What a compile produced. On failure Module is null and the diagnostics say which nodes to fix; a graph
// that fails to compile does not run.
public sealed class CompileResult
{
    public string GraphHash { get; }
    public byte[]? Module { get; }
    public byte[] Manifest { get; }
    public IReadOnlyList<CompileDiagnostic> Diagnostics { get; }
    public IReadOnlyList<NodeOverload> Imports { get; }
    public IReadOnlyList<ScriptEventEntry> Events { get; }
    public IReadOnlyList<ScriptTimerEntry> Timers { get; }
    public IReadOnlyList<ScriptVariableEntry> Variables { get; }
    public IReadOnlyList<ScriptBindingEntry> Bindings { get; }
    public IReadOnlyList<ScriptDisplayEntry> Displays { get; }

    // Source map tag to node id: a trap reports the tag of the node it happened in.
    public IReadOnlyList<string> TagNodes { get; }

    // One per wired flow exit, for the editor's live view.
    public IReadOnlyList<ScriptTraceEntry> Traces { get; init; } = Array.Empty<ScriptTraceEntry>();

    // One per Once, Toggle and Cooldown, for a live graph swap to carry over.
    public IReadOnlyList<ScriptMemoryEntry> FlowMemory { get; init; } = Array.Empty<ScriptMemoryEntry>();

    // One per await node, with the export each answer runs.
    public IReadOnlyList<ScriptAwaitEntry> Awaits { get; init; } = Array.Empty<ScriptAwaitEntry>();

    // The module as the engine's validator decoded it when the compiler checked its own output. Nothing
    // writes a module once it is decoded, so every instance of this graph runs this one, from any thread.
    internal WasmModule? Decoded { get; init; }

    internal CompileResult(string graphHash, byte[]? module, byte[] manifest, IReadOnlyList<CompileDiagnostic> diagnostics,
        IReadOnlyList<NodeOverload> imports, IReadOnlyList<ScriptEventEntry> events, IReadOnlyList<ScriptTimerEntry> timers,
        IReadOnlyList<ScriptVariableEntry> variables,
        IReadOnlyList<ScriptBindingEntry> bindings, IReadOnlyList<string> tagNodes,
        IReadOnlyList<ScriptDisplayEntry>? displays = null)
    {
        GraphHash = graphHash;
        Module = module;
        Manifest = manifest;
        Diagnostics = diagnostics;
        Imports = imports;
        Events = events;
        Timers = timers;
        Variables = variables;
        Bindings = bindings;
        TagNodes = tagNodes;
        Displays = displays ?? Array.Empty<ScriptDisplayEntry>();
    }

    public bool Success => Module != null && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);

    public IEnumerable<CompileDiagnostic> Errors => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);

    // A compile that could not run at all, and why.
    public static CompileResult Unrun(string graphHash, string message)
        => Failed(graphHash, new[] { new CompileDiagnostic(DiagnosticSeverity.Error, null, message) });

    internal static CompileResult Failed(string graphHash, IReadOnlyList<CompileDiagnostic> diagnostics)
        => new(graphHash, null, Array.Empty<byte>(), diagnostics, Array.Empty<NodeOverload>(), Array.Empty<ScriptEventEntry>(),
            Array.Empty<ScriptTimerEntry>(), Array.Empty<ScriptVariableEntry>(), Array.Empty<ScriptBindingEntry>(), Array.Empty<string>());
}
