// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Warden;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core;

// The gate's view of RefID. RefID stays here; the gate is handed only the bit rules it
// needs, so it holds no engine type and the ownership RULE (authority ids are owned by nobody, a user
// owns what was minted in their allocation byte) stays on the gate's side of the wall. -xlinka
internal sealed class RefIDPermissionIdSpace : IPermissionIdSpace
{
    public static readonly RefIDPermissionIdSpace Instance = new();

    private RefIDPermissionIdSpace() { }

    public bool IsNull(ulong id) => new RefID(id).IsNull;

    public bool IsAuthority(ulong id) => new RefID(id).IsAuthorityID;

    public byte OwnerByte(ulong id) => new RefID(id).GetUserByte();

    public bool IsValidOwnerByte(byte ownerByte) => RefIDConstants.IsValidUserByte(ownerByte);

    public bool TryGetElementId(object? key, out ulong id)
    {
        if (key is RefID refId)
        {
            id = refId.RawValue;
            return true;
        }

        id = 0;
        return false;
    }
}

// The engine's handle on a world's permission gate. The decision itself is
// PermissionEngine over in LumoraWarden; this only forwards to it and FAILS CLOSED if
// the gate ever throws. There is no state here that could disagree with the gate, and no path that
// answers a request without asking it. -xlinka
public sealed class DataModelPermissionController
{
    // Building the gate is part of building the world: if it cannot be built the world constructor
    // throws and no world exists to write to, which is the fail-closed outcome. Kept private so the
    // only way into the policy from here is through the forwarding members below. -xlinka
    private readonly PermissionEngine _engine;

    public DataModelPermissionController(World world)
    {
        if (world == null)
        {
            throw new ArgumentNullException(nameof(world));
        }

        _engine = new PermissionEngine(world, RefIDPermissionIdSpace.Instance)
        {
            Warn = LumoraLogger.Warn
        };
    }

    public DataModelPermissionRole HostRole => _engine.HostRole;
    public DataModelPermissionRole AdminRole => _engine.AdminRole;
    public DataModelPermissionRole BuilderRole => _engine.BuilderRole;
    public DataModelPermissionRole ModeratorRole => _engine.ModeratorRole;
    public DataModelPermissionRole GuestRole => _engine.GuestRole;
    public DataModelPermissionRole SpectatorRole => _engine.SpectatorRole;
    public IReadOnlyList<DataModelPermissionRole> AssignableRoles => _engine.AssignableRoles;

    // Master switch for permission enforcement. HOST-AUTHORITATIVE and FAIL-CLOSED: only the authority
    // may turn enforcement off, and the refusal is decided inside the gate, not here.
    public bool Enabled
    {
        get => _engine.Enabled;
        set => _engine.Enabled = value;
    }

    public bool LogDeniedMutations
    {
        get => _engine.LogDeniedMutations;
        set => _engine.LogDeniedMutations = value;
    }

    // Social/Event lockdown. When set, the authored world is frozen for EVERYONE (including the host
    // and admins): only a user's OWN runtime objects (avatar, spawned items) may be mutated. World
    // content is created under the authority RefID, so no user "owns" it and all editing of it is
    // denied - there's no role that escapes this and no live toggle. Set by WorldModePermissions.
    public bool SocialLock
    {
        get => _engine.SocialLock;
        set => _engine.SocialLock = value;
    }

    public DataModelPermissionRole GetDefaultRole(DataModelAccessClass accessClass) => _engine.GetDefaultRole(accessClass);

    public void SetDefaultRole(DataModelAccessClass accessClass, DataModelPermissionRole role) => _engine.SetDefaultRole(accessClass, role);

    public int UserOverrideCount => _engine.UserOverrideCount;

    public void ClearUserOverrides() => _engine.ClearUserOverrides();

    public IDisposable EnterActor(User? actor) => _engine.EnterActor(actor);

    public IDisposable EnterSystemBypass() => _engine.EnterSystemBypass();

    public void AddRule(IDataModelPermissionRule rule) => _engine.AddRule(rule);

    public bool RemoveRule(IDataModelPermissionRule rule) => _engine.RemoveRule(rule);

    public void ClearRules() => _engine.ClearRules();

    public void SetUserRole(User user, DataModelPermissionRole role) => _engine.SetUserRole(user, role);

    public void ClearUserRole(User user) => _engine.ClearUserRole(user);

    public DataModelPermissionRole GetRole(User? user) => _engine.GetRole(user);

    public DataModelAccessClass GetAccessClass(User? user) => _engine.GetAccessClass(user);

    public IReadOnlyList<DataModelPermissionRole> AssignableRolesFor(WorldMode mode) => _engine.AssignableRolesFor(mode);

    // The Social/Event lock floor plus the default role per access class.
    public void ApplyMode(WorldMode mode) => _engine.ApplyMode(mode);

    // Ask the gate. A gate that throws is a gate that did not decide, so it denies: an exception here
    // must never read as permission. -xlinka
    public bool Authorize(in DataModelPermissionRequest request, out string? reason)
    {
        try
        {
            return _engine.Authorize(in request, out reason);
        }
        catch (Exception ex)
        {
            reason = $"permission check failed: {ex.Message}";
            LumoraLogger.Error($"Datamodel permission check threw, denying: {ex}");
            return false;
        }
    }

    public void Assert(in DataModelPermissionRequest request)
    {
        if (!Authorize(request, out var reason))
        {
            throw new UnauthorizedAccessException(reason ?? "datamodel mutation denied");
        }
    }
}
