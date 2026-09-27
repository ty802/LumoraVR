// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// How one event call ended. NodeId is the node the call stopped in, from the module's source map.
public readonly record struct ScriptCallResult(ScriptCallStatus Status, string? NodeId, string? Message, long FuelUsed)
{
    public bool Ok => Status == ScriptCallStatus.Ok;

    public override string ToString()
        => Status == ScriptCallStatus.Ok ? $"Ok ({FuelUsed} fuel)" : $"{Status} at {NodeId ?? "?"}: {Message}";
}
