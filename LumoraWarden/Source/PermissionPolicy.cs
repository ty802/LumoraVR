// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Warden;

// A host's configuration, as the gate reads it. These are plain carriers: they hold what the host typed,
// never what it means. Every one of them is turned into a resolved decision by PermissionEngine, and the
// engine takes a whole snapshot at once (ApplyPolicy) rather than reading the datamodel per request - so
// a live config edit costs one rebuild, and Authorize keeps touching nothing but dictionaries. -xlinka

// One user pinned to one role, keyed by durable identity. AccountKey wins when both sides have one,
// because it survives the user changing machines; MachineKey is the fallback that always exists.
public sealed class PermissionAssignment
{
    public string? MachineKey;
    public string? AccountKey;
    public string? RoleName;
}

// Per-role overrides of the compiled capability caps. Inherit leaves the compiled answer alone; the
// scale bounds are inactive at zero or less.
public sealed class PermissionRoleCap
{
    public string? RoleName;
    public PermissionToggle Spawn;
    public PermissionToggle SaveCopy;
    public PermissionToggle Export;
    public PermissionToggle ToolUse;
    public PermissionToggle Touch;
    public float MinScale;
    public float MaxScale;
}

public sealed class PermissionEscalationSettings
{
    // How long a denial keeps counting. The score halves every this many seconds, so a peer that trips
    // the gate once an hour never accumulates and one that trips it constantly climbs fast.
    public double HalfLifeSeconds = 60.0;

    // Log a warning for the host at this score. One ownership violation is worth 10.
    public double WarnScore = 10.0;

    public double KickScore = 50.0;

    public double TempBanScore = 200.0;

    // The most the host is willing to do about it. Kick by default: a temp ban is a real punishment and
    // the host should have to ask for it. -xlinka
    public PermissionViolationResponse Response = PermissionViolationResponse.Kick;
}

public sealed class PermissionPolicy
{
    // Role a joiner with no assignment lands on. Empty falls back to the per-access-class default the
    // world mode baked in, which is never wider.
    public string? DefaultJoinerRole;

    public readonly List<PermissionAssignment> Assignments = new();

    public readonly List<PermissionRoleCap> Caps = new();

    public PermissionEscalationSettings Escalation = new();
}
