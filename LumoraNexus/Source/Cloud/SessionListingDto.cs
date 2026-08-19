// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Lumora.Nexus.Cloud;

// One session as the backend directory returns it. This is the WHOLE contract: every member here is a
// field GET /api/sessions actually sends, so anything the UI renders off this object is real. If a field
// is not on this class the directory does not know it, and nothing may invent it. -xlinka
public sealed class SessionListingDto
{
    public string SessionId { get; set; } = "";

    // Assigned by the backend from the host's token, not host-supplied.
    public string HostUserId { get; set; } = "";

    // Also from the host's token, so it cannot be spoofed.
    public string HostUsername { get; set; } = "";

    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    // Host-advertised connection URLs, best first.
    public List<string> SessionUrls { get; set; } = new();

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SessionVisibility AccessLevel { get; set; } = SessionVisibility.Public;

    public int MaxUsers { get; set; }
    public int ActiveUsers { get; set; }
    public List<string> UserList { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public bool IsHeadless { get; set; }
    public string AppVersion { get; set; } = "";

    // Empty when the host did not publish one.
    public string CompatibilityHash { get; set; } = "";

    public string? ThumbnailUrl { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime LastHeartbeatUtc { get; set; }
    public long UptimeSeconds { get; set; }
}
