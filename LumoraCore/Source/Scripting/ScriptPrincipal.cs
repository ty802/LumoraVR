// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Warden;

namespace Lumora.Core.Scripting;

// Who a script acts as. Resolved once, when the instance starts, from the RefID byte of the thing
// carrying the script:
//   byte 0      the world itself: world content only, in every world mode;
//   a user byte the user who owns that byte, pinned to their durable identity.
// It never falls back to the ambient actor, which on the host is the host. A user principal carries the
// author's id, byte and keys, so their role and denial ledger apply, but never their root or host
// status. If the author leaves, or the byte comes to belong to someone else, the script stops. -xlinka
public sealed class ScriptPrincipal : IPermissionActor
{
    public static ScriptPrincipal WorldContent { get; } = new(0, RefIDConstants.AUTHORITY_BYTE, "world script", null, null, worldContent: true);

    public ulong Id { get; }
    public byte AllocationByte { get; }
    public string? DisplayName { get; }
    public string? MachineKey { get; }
    public string? AccountKey { get; }
    public bool IsWorldContent { get; }
    public bool IsHost => false;

    // Who counts as the same author in what the ledger counts: a proven account where there is one, so the
    // same person on two machines counts once, else the user record of this session. The world's own
    // scripts are one author between them.
    public string AuthorKey { get; }
    public IPermissionTarget? RootElement => null;

    // The user record behind a user principal, for the questions the gate asks of a User. Null for the
    // world's own scripts.
    public User? Author { get; private init; }

    private ScriptPrincipal(ulong id, byte allocationByte, string? displayName, string? machineKey, string? accountKey, bool worldContent)
    {
        Id = id;
        AllocationByte = allocationByte;
        DisplayName = displayName;
        MachineKey = machineKey;
        AccountKey = accountKey;
        IsWorldContent = worldContent;
        AuthorKey = worldContent ? "world" : KeyOf(id, accountKey);
    }

    public static string AuthorKeyOf(User user) => KeyOf(user.ReferenceID.RawValue, user.AccountId.Value);

    private static string KeyOf(ulong id, string? accountKey)
        => !string.IsNullOrEmpty(accountKey) ? "account:" + accountKey : "user:" + id;

    public static ScriptPrincipal ForUser(User author)
        => new(author.ReferenceID.RawValue, author.AllocationID.Value, $"{author.UserName.Value} (script)",
            author.MachineID.Value, author.AccountId.Value, worldContent: false) { Author = author };

    // The author of whatever carries the script, or null when nobody present owns its byte.
    public static ScriptPrincipal? ForElement(IWorldElement element)
    {
        var world = element?.World;
        if (world == null)
            return null;

        byte owner = element!.ReferenceID.GetUserByte();
        if (owner == RefIDConstants.AUTHORITY_BYTE)
            return WorldContent;
        if (!RefIDConstants.IsValidUserByte(owner))
            return null;

        foreach (var user in world.GetAllUsers())
        {
            if (user.AllocationID.Value == owner && !user.IsDestroyed)
                return ForUser(user);
        }
        return null;
    }

    // Whether the author this principal was pinned to is still the one behind its byte.
    public bool IsStillAuthorIn(World world)
    {
        if (IsWorldContent)
            return true;

        foreach (var user in world.GetAllUsers())
        {
            if (user.AllocationID.Value != AllocationByte || user.IsDestroyed)
                continue;
            return user.ReferenceID.RawValue == Id
                && user.MachineID.Value == MachineKey
                && user.AccountId.Value == AccountKey;
        }
        return false;
    }

    public override string ToString() => DisplayName ?? $"principal {Id}";
}
