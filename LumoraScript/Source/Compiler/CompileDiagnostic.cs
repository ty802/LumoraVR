// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// A problem with a graph, pinned to the node that caused it so the editor can badge that node. NodeId
// is null for a problem with the graph as a whole. NeedsNewerBuild marks a node this build does not
// have, or has only in an older version: the graph is fine, the machine reading it is behind.
public sealed record CompileDiagnostic(DiagnosticSeverity Severity, string? NodeId, string Message)
{
    public bool NeedsNewerBuild { get; init; }

    public override string ToString() => NodeId == null ? $"{Severity}: {Message}" : $"{Severity} at {NodeId}: {Message}";
}
