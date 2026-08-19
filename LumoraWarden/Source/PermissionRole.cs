// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Warden;

public sealed class PermissionRole
{
    public string Name { get; }
    public PermissionAction AllowedOwnActions { get; set; }
    public PermissionAction AllowedForeignActions { get; set; }

    public PermissionRole(string name, PermissionAction allowedOwnActions, PermissionAction allowedForeignActions)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "Unnamed" : name;
        AllowedOwnActions = allowedOwnActions;
        AllowedForeignActions = allowedForeignActions;
    }

    public bool Allows(PermissionAction action, bool ownsTarget)
    {
        var allowed = ownsTarget ? AllowedOwnActions : AllowedForeignActions;
        return (allowed & action) == action;
    }

    public override string ToString() => Name;
}
