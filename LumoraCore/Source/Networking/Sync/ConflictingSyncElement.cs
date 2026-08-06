// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using Lumora.Core;

namespace Lumora.Core.Networking.Sync;

public abstract class ConflictingSyncElement : SyncElement
{
    // Dynamic by default; override in concrete classes.
    public override SyncMemberType MemberType => SyncMemberType.Dynamic;
    protected new enum InternalFlags
    {
        IsValid = 13,
        IsHostOnly = 14,
        DirectAccessOnly = 15,
        END = 16
    }

    private bool _isValid
    {
        get => GetFlag((int)InternalFlags.IsValid);
        set => SetFlag((int)InternalFlags.IsValid, value);
    }

    // Invalid elements have hit conflicts and await resync.
    public override bool IsValid => _isValid;

    public bool IsHostOnly
    {
        get => GetFlag((int)InternalFlags.IsHostOnly);
        private set => SetFlag((int)InternalFlags.IsHostOnly, value);
    }

    public bool DirectAccessOnly
    {
        get => GetFlag((int)InternalFlags.DirectAccessOnly);
        private set => SetFlag((int)InternalFlags.DirectAccessOnly, value);
    }

    public ulong LastHostVersion { get; private set; }

    // Host tick on the server, sync tick on the client.
    public ulong LastVersion { get; private set; }

    public ulong LastConfirmedTime { get; private set; }

    public User LastModifyingUser { get; private set; } = null!;

    // The authority is always confirmed; guests need confirmation from the host.
    public virtual bool IsConfirmed
    {
        get
        {
            if (World?.IsAuthority == true)
                return true;
            if (IsSyncDirty)
                return false;
            return LastVersion == LastConfirmedTime;
        }
    }

    public event Action Invalidated = null!;

    public ConflictingSyncElement()
    {
        _isValid = true;
    }

    public bool WasLastModifiedBy(User user)
    {
        if (user == LastModifyingUser)
            return true;
        if (user == World?.LocalUser && IsSyncDirty)
            return true;
        return false;
    }

    public void MarkHostOnly()
    {
        IsHostOnly = true;
        DirectAccessOnly = true;
    }

    public void MarkDirectAccessOnly()
    {
        DirectAccessOnly = true;
    }

    public override MessageValidity Validate(BinaryMessageBatch inboundMessage, BinaryReader reader, List<ValidationGroup.Rule> rules)
    {
        if (!IsValid)
            return MessageValidity.Conflict;

        if (IsDriven)
            return MessageValidity.Ignore;

        if (World?.IsAuthority == true)
        {
            if (IsHostOnly)
                return MessageValidity.Conflict;

            bool messageNewer;
            if (inboundMessage.SenderUser != LastModifyingUser)
            {
                messageNewer = inboundMessage.SenderStateVersion >= LastHostVersion;
            }
            else
            {
                messageNewer = inboundMessage.SenderSyncTick > LastVersion;
            }

            if (!messageNewer)
                return MessageValidity.Conflict;

            var surface = this is IField ? DataModelPermissionSurface.Field : DataModelPermissionSurface.SyncElement;
            if (!AuthorizeDataModelMutation(
                    DataModelPermissionAction.Write | DataModelPermissionAction.Replicate,
                    surface,
                    inboundMessage.SenderUser,
                    isNetwork: true,
                    throwOnError: false))
            {
                return MessageValidity.Conflict;
            }

            return MessageValidity.Valid;
        }

        _isValid = IsConfirmed;
        if (!_isValid)
            return MessageValidity.Conflict;

        return MessageValidity.Valid;
    }

    public override void Invalidate()
    {
        if (World?.IsAuthority != true)
        {
            _isValid = false;
            Invalidated?.Invoke();
        }
    }

    public override void Confirm(ulong confirmSyncTime)
    {
        if (confirmSyncTime <= LastConfirmedTime)
            throw new InvalidOperationException("Invalid confirmation: time must be greater than last confirmed time");
        LastConfirmedTime = confirmSyncTime;
    }

    public override void EncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        base.EncodeFull(writer, outboundMessage);
        if (World?.IsAuthority == true)
        {
            _isValid = true;
        }
    }

    public override void DecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        base.DecodeFull(reader, inboundMessage);
        _isValid = true;
        LastHostVersion = inboundMessage.SenderStateVersion;
    }

    public override void EncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        if (!IsValid)
            throw new InvalidOperationException("Cannot Delta Encode an invalid SyncElement!");

        if (World?.IsAuthority == true)
        {
            LastHostVersion = World.StateVersion;
            LastVersion = World.StateVersion;
            LastModifyingUser = World.LocalUser;
        }
        else
        {
            LastVersion = World?.SyncTick ?? 0;
        }

        base.EncodeDelta(writer, outboundMessage);
    }

    public override void DecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        if (World?.IsAuthority == true)
        {
            LastModifyingUser = inboundMessage.SenderUser;
            LastVersion = inboundMessage.SenderSyncTick;
            LastHostVersion = World.StateVersion;
        }
        else
        {
            LastHostVersion = inboundMessage.SenderSyncTick;
        }

        base.DecodeDelta(reader, inboundMessage);
    }

    public override void Dispose()
    {
        LastModifyingUser = null!;
        Invalidated = null!;
        base.Dispose();
    }
}
