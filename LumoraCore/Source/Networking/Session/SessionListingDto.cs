// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Networking.Session;

/// <summary>
/// Public session listing contract used by discovery clients.
/// </summary>
public sealed class SessionListingDto
{
    public string Name { get; set; } = "";
    public string SessionIdentifier { get; set; } = "";
    public string WorldIdentifier { get; set; } = "";
    public string HostUsername { get; set; } = "";
    public int ActiveUsers { get; set; }
    public int MaxUsers { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string[] UserList { get; set; } = Array.Empty<string>();
    public bool IsHeadless { get; set; }
    public string Version { get; set; } = "";
    public string VersionHash { get; set; } = "";
    public long UptimeSeconds { get; set; }
    public bool Direct { get; set; }
    public bool HasDirect { get; set; }
    public bool HasNat { get; set; } = true;
    public bool HasRelay { get; set; }
    public string Region { get; set; } = "default";
    public string? ThumbnailUrl { get; set; }
    public SessionConnectionEndpointDto[] Endpoints { get; set; } = Array.Empty<SessionConnectionEndpointDto>();
}

public sealed class SessionConnectionEndpointDto
{
    public string Kind { get; set; } = "nat";
    public string Url { get; set; } = "";
    public int Priority { get; set; } = 100;
    public string? Region { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
