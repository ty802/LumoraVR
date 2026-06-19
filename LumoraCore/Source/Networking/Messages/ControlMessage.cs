// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Lumora.Core.Networking;

namespace Lumora.Core.Networking.Messages;

/// <summary>
/// Control message types for session management.
/// </summary>
public enum ControlMessageType : byte
{
    /// <summary>
    /// Client requests to join the session.
    /// </summary>
    JoinRequest = 0,

    /// <summary>
    /// Authority grants join permission and provides initial data.
    /// Contains: UserID, MaxUsers, WorldTime, StateVersion
    /// </summary>
    JoinGrant = 1,

    /// <summary>
    /// Authority signals that full state has been sent, client can start receiving deltas.
    /// </summary>
    JoinStartDelta = 2,

    /// <summary>
    /// User is leaving the session.
    /// </summary>
    Leave = 3,

    /// <summary>
    /// Authority kicked a user.
    /// </summary>
    Kick = 4,

    /// <summary>
    /// Ping/keepalive message.
    /// </summary>
    Ping = 5,

    /// <summary>
    /// Pong response to ping.
    /// </summary>
    Pong = 6
}

/// <summary>
/// Control message for session management.
/// </summary>
public class ControlMessage
{
    public ControlMessageType SubType;
    public byte[] Data = null!;

    public MessageType Type => MessageType.Control;
    public bool Reliable => true;

    public byte[] Encode()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write((byte)Type);
        writer.Write((byte)SubType);

        if (Data != null && Data.Length > 0)
        {
            writer.Write(Data.Length);
            writer.Write(Data);
        }
        else
        {
            writer.Write(0);
        }

        return ms.ToArray();
    }

    public static ControlMessage Decode(BinaryReader reader)
    {
        var message = new ControlMessage
        {
            SubType = (ControlMessageType)reader.ReadByte()
        };

        // Bounded read: a peer cannot make us allocate >MaxControlMessagePayload
        // by declaring a huge length. Empty payloads are allowed (length == 0).
        message.Data = reader.ReadBoundedBytesInt32(NetworkLimits.MaxControlMessagePayload);
        return message;
    }
}

/// <summary>
/// Join request data sent by client when connecting.
/// </summary>
public struct JoinRequestData
{
    public string UserName;
    public string MachineID;
    public string UserID;
    public byte HeadDevice;

    /// <summary>
    /// DER SubjectPublicKeyInfo for this client's machine key. MachineID must be the hash of this, and
    /// the joiner has to sign the host's challenge nonce with the matching private key to get in. This
    /// is what makes MachineID actually mean something instead of being free text. -xlinka
    /// </summary>
    public byte[] MachinePublicKey;

    /// <summary>
    /// Optional account identity. When the joiner is signed into an account, these name the
    /// account and the login session whose public key it published to the backend. The host fetches that
    /// key from the cloud and verifies the account signature (in JoinAuthenticate). Empty = guest, machine
    /// key only. We do NOT send the account public key itself, the host gets the trusted one from the
    /// cloud so a joiner can't present a key of their choosing. -xlinka
    /// </summary>
    public string AccountUserId;
    public string AccountSessionId;

    public byte[] Encode()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(UserName ?? "");
        writer.Write(MachineID ?? "");
        writer.Write(UserID ?? "");
        writer.Write(HeadDevice);
        writer.Write(MachinePublicKey?.Length ?? 0);
        if (MachinePublicKey != null && MachinePublicKey.Length > 0)
            writer.Write(MachinePublicKey);
        writer.Write(AccountUserId ?? "");
        writer.Write(AccountSessionId ?? "");

        return ms.ToArray();
    }

    public static JoinRequestData Decode(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        var result = new JoinRequestData
        {
            UserName = reader.ReadString(),
            MachineID = reader.ReadString(),
            UserID = reader.ReadString(),
            HeadDevice = reader.ReadByte(),
            MachinePublicKey = Array.Empty<byte>(),
            AccountUserId = "",
            AccountSessionId = ""
        };

        // Everything past HeadDevice is appended and read tolerantly so a short/legacy payload still
        // decodes. The key length is bounded so a hostile peer can't make us allocate off a declared size.
        // -xlinka
        if (ms.Position < ms.Length)
        {
            int keyLen = reader.ReadInt32();
            if (keyLen > 0 && keyLen <= 8192)
                result.MachinePublicKey = reader.ReadBytes(keyLen);
        }
        if (ms.Position < ms.Length)
            result.AccountUserId = reader.ReadString();
        if (ms.Position < ms.Length)
            result.AccountSessionId = reader.ReadString();

        return result;
    }
}

/// <summary>
/// Join grant data sent by authority.
/// </summary>
public struct JoinGrantData
{
    public ulong AssignedUserID;
    public ulong AllocationIDStart;
    public ulong AllocationIDEnd;
    public int MaxUsers;
    public double WorldTime;
    public ulong StateVersion;

    public byte[] Encode()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(AssignedUserID);
        writer.Write(AllocationIDStart);
        writer.Write(AllocationIDEnd);
        writer.Write(MaxUsers);
        writer.Write(WorldTime);
        writer.Write(StateVersion);

        return ms.ToArray();
    }

    public static JoinGrantData Decode(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        return new JoinGrantData
        {
            AssignedUserID = reader.ReadUInt64(),
            AllocationIDStart = reader.ReadUInt64(),
            AllocationIDEnd = reader.ReadUInt64(),
            MaxUsers = reader.ReadInt32(),
            WorldTime = reader.ReadDouble(),
            StateVersion = reader.ReadUInt64()
        };
    }
}

/// <summary>
/// Join rejection data sent by authority when a user cannot enter a session.
/// </summary>
public struct JoinRejectData
{
    public string Reason;

    public byte[] Encode()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(Reason ?? "Join rejected");

        return ms.ToArray();
    }

    public static JoinRejectData Decode(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        return new JoinRejectData
        {
            Reason = reader.ReadString()
        };
    }
}

/// <summary>
/// Host -> joiner: a random nonce the joiner must sign with its machine private key to prove it holds
/// the key behind the MachineID it claimed. Fresh per join, so a captured signature can't be replayed
/// to another join (different nonce). -xlinka
/// </summary>
public struct JoinChallengeData
{
    public byte[] Nonce;

    public byte[] Encode()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(Nonce?.Length ?? 0);
        if (Nonce != null && Nonce.Length > 0)
            writer.Write(Nonce);

        return ms.ToArray();
    }

    public static JoinChallengeData Decode(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        var result = new JoinChallengeData { Nonce = Array.Empty<byte>() };
        int len = reader.ReadInt32();
        if (len > 0 && len <= 1024)
            result.Nonce = reader.ReadBytes(len);
        return result;
    }
}

/// <summary>
/// Joiner -> host: the joiner's signature over the challenge nonce, made with its machine private key.
/// The host verifies it against the public key from the JoinRequest. -xlinka
/// </summary>
public struct JoinAuthenticateData
{
    public byte[] Signature;          // machine-key signature over the challenge nonce
    public byte[] AccountSignature;   // optional account-key signature over the same nonce, when signed in

    public byte[] Encode()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(Signature?.Length ?? 0);
        if (Signature != null && Signature.Length > 0)
            writer.Write(Signature);
        writer.Write(AccountSignature?.Length ?? 0);
        if (AccountSignature != null && AccountSignature.Length > 0)
            writer.Write(AccountSignature);

        return ms.ToArray();
    }

    public static JoinAuthenticateData Decode(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        var result = new JoinAuthenticateData
        {
            Signature = Array.Empty<byte>(),
            AccountSignature = Array.Empty<byte>()
        };
        int len = reader.ReadInt32();
        if (len > 0 && len <= 4096)
            result.Signature = reader.ReadBytes(len);
        // Account signature is appended; tolerate its absence (guest / legacy). -xlinka
        if (ms.Position < ms.Length)
        {
            int accLen = reader.ReadInt32();
            if (accLen > 0 && accLen <= 4096)
                result.AccountSignature = reader.ReadBytes(accLen);
        }
        return result;
    }
}
