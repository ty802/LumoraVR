// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// One compile on this machine, kept beside the cached result for as long as the cache holds it, and
// whether it ran on a worker.
public readonly record struct ScriptCompileRecord(string Hash, double Milliseconds, int ModuleBytes, int Nodes, bool Success,
    bool OnWorker = false);

// The compile cache as a whole: fresh compiles, hits, what they cost, and how long the node registry
// took to scan on first use.
public readonly record struct ScriptCompileStats(int Compiles, int CacheHits, int Cached, double TotalMs, double SlowestMs,
    ScriptCompileRecord Last, double RegistryScanMs);
