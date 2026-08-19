// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Nexus.Cloud;

public enum SessionVisibility
{
    Private,

    LAN,

    Contacts,

    Public
}

public class SessionMetadata
{
    // Format: S-{guid}
    public string SessionId { get; set; } = null!;

    public List<Uri> SessionURLs { get; set; } = new();

    public string HostUserId { get; set; } = null!;

    public string HostUsername { get; set; } = null!;

    public string HostMachineId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Description { get; set; } = null!;

    public SessionVisibility Visibility { get; set; } = SessionVisibility.Private;

    public int MaxUsers { get; set; } = 16;

    public int ActiveUsers { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime LastUpdate { get; set; }

    public bool IsHeadless { get; set; }

    public bool HideFromListing { get; set; }

    public string ThumbnailUrl { get; set; } = null!;

    // PNG, base64. Used by LAN discovery, typically 256x144.
    public string? ThumbnailBase64 { get; set; }

    public string VersionHash { get; set; } = null!;

    public List<string> Tags { get; set; } = new();

    public SessionMetadata Clone()
    {
        return new SessionMetadata
        {
            SessionId = SessionId,
            SessionURLs = new List<Uri>(SessionURLs),
            HostUserId = HostUserId,
            HostUsername = HostUsername,
            HostMachineId = HostMachineId,
            Name = Name,
            Description = Description,
            Visibility = Visibility,
            MaxUsers = MaxUsers,
            ActiveUsers = ActiveUsers,
            StartTime = StartTime,
            LastUpdate = LastUpdate,
            IsHeadless = IsHeadless,
            HideFromListing = HideFromListing,
            ThumbnailUrl = ThumbnailUrl,
            ThumbnailBase64 = ThumbnailBase64,
            VersionHash = VersionHash,
            Tags = new List<string>(Tags)
        };
    }
}
