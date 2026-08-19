// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Nexus;

// Facts about the running host that this assembly has to put on the wire but cannot look up itself.
// Nexus sits below the engine, so the host pushes them down at startup instead. Keep this to values
// that are fixed for the process; anything that can change while a session runs belongs behind a
// provider on the type that needs it, not a snapshot here. -xlinka
public static class NexusRuntime
{
    // Build version published to session directories and browsers. The host sets it during startup;
    // the placeholder only shows up if something ran a transport before the engine came up.
    public static string AppVersion { get; set; } = "0.0.0-unknown";
}
