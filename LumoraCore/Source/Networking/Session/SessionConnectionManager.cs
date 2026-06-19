// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Lumora.Core;
using Lumora.Core.Networking;
using Lumora.Core.Networking.Sync;
using LegacyJoinGrantData = Lumora.Core.Networking.Messages.JoinGrantData;
using LegacyJoinRequestData = Lumora.Core.Networking.Messages.JoinRequestData;
using LegacyJoinRejectData = Lumora.Core.Networking.Messages.JoinRejectData;
using LegacyJoinChallengeData = Lumora.Core.Networking.Messages.JoinChallengeData;
using LegacyJoinAuthenticateData = Lumora.Core.Networking.Messages.JoinAuthenticateData;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking.Session;

/// <summary>
/// Manages connections and maps them to users.
/// 
/// </summary>
public class SessionConnectionManager : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<IConnection, User> _connectionToUser = new();
    private readonly Dictionary<User, IConnection> _userToConnection = new();
    private readonly HashSet<IConnection> _pendingConnections = new(); // Connections waiting for JoinRequest
    private readonly Dictionary<string, int> _pendingByIp = new();     // IP -> count of pending connections

    // Connections that sent a valid JoinRequest and got a challenge, now waiting on the signed answer.
    // Holds the original request + the nonce we asked them to sign. Cleaned on grant, reject, or
    // disconnect, and counted against the pending cap so it can't be used to dodge the DoS limit. -xlinka
    private readonly Dictionary<IConnection, PendingJoin> _pendingAuth = new();

    private sealed class PendingJoin
    {
        public LegacyJoinRequestData Request;
        public byte[] Nonce = null!;
    }

    // Domain-separation labels so a signature made for one step of the handshake can't be replayed as
    // another (host-proving vs joiner-proving vs account-proving). Both sides build payloads with these. -xlinka
    private const string JoinContextHost = "lumora-join-host";
    private const string JoinContextUserMachine = "lumora-join-user-machine";
    private const string JoinContextUserAccount = "lumora-join-user-account";

    // Client side: the nonce we sent in our JoinRequest for the host to sign, so we can verify the host is
    // who it claims before we hand over our own signature. -xlinka
    private byte[]? _hostVerificationToken;

    public Session Session { get; private set; }
    public World World => (Session?.World) ?? null!;

    public IListener Listener { get; private set; } = null!;
    public IConnection HostConnection { get; private set; } = null!;

    /// <summary>
    /// Event triggered when host connection is lost (client side).
    /// </summary>
    public event Action OnHostDisconnected = null!;

    public SessionConnectionManager(Session session)
    {
        Session = session;
    }

    /// <summary>
    /// Start listening for connections (host only). Picks the
    /// highest-priority registered <see cref="INetworkManager"/> as the
    /// transport. Override the scheme with <paramref name="preferredScheme"/>
    /// to pin a specific transport (e.g. "lnl" for direct UDP). - xlinka
    /// </summary>
    public bool StartListener(ushort port, string preferredScheme = null!)
    {
        if (Listener != null)
        {
            throw new InvalidOperationException("Listener already started");
        }

        var manager = preferredScheme != null
            ? NetworkManagerRegistry.FindForScheme(preferredScheme)
            : NetworkManagerRegistry.Managers.FirstOrDefault();

        if (manager == null)
        {
            LumoraLogger.Error($"StartListener: no network manager registered (scheme={preferredScheme ?? "any"})");
            return false;
        }

        var sessionId = Session?.Metadata?.SessionId;
        Listener = manager.CreateListener(port, sessionId!);

        if (Listener == null || !Listener.IsActive)
        {
            LumoraLogger.Error($"Failed to start {manager.GetType().Name} listener on port {port}");
            return false;
        }

        Listener.PeerConnected += OnPeerConnected;
        Listener.PeerDisconnected += OnPeerDisconnected;

        LumoraLogger.Log($"Listener started ({manager.GetType().Name}, port {port}, sessionId={sessionId ?? "(none)"})");
        return true;
    }

    /// <summary>
    /// Connect to host as client.
    /// </summary>
    public async Task<bool> ConnectToAsync(IEnumerable<Uri> addresses)
    {
        var uri = addresses.FirstOrDefault();
        if (uri == null)
        {
            LumoraLogger.Error("No addresses provided");
            return false;
        }

        LumoraLogger.Log($"Connecting to {uri}");

        var manager = NetworkManagerRegistry.FindForUri(uri);
        if (manager == null)
        {
            LumoraLogger.Error($"ConnectToAsync: no registered network manager handles scheme '{uri.Scheme}'");
            return false;
        }

        var connection = manager.CreateConnection(uri);

        var taskCompletionSource = new TaskCompletionSource<bool>();

        connection.Connected += (c) =>
        {
            LumoraLogger.Log($"Connected to {c.Address}");
            taskCompletionSource.TrySetResult(true);
        };

        connection.ConnectionFailed += (c) =>
        {
            LumoraLogger.Error($"Connection failed: {c.FailReason}");
            taskCompletionSource.TrySetResult(false);
        };

        connection.Closed += (c) =>
        {
            LumoraLogger.Warn($"Connection closed: {c.FailReason}");
            taskCompletionSource.TrySetResult(false);
            
            // Trigger host disconnected event if this was the host connection
            if (HostConnection == c)
            {
                OnHostDisconnected?.Invoke();
            }
        };

        connection.DataReceived += (data, length) => OnConnectionDataReceived(connection, data, length);
        connection.Connect(null!);

        var timeoutTask = Task.Delay(10000);
        var completedTask = await Task.WhenAny(taskCompletionSource.Task, timeoutTask);
        bool success = completedTask == taskCompletionSource.Task && taskCompletionSource.Task.Result;

        if (success)
        {
            HostConnection = connection;

            // Send JoinRequest to host with our username
            SendJoinRequest();
            return true;
        }

        if (completedTask == timeoutTask)
        {
            LumoraLogger.Warn($"Connection timed out: {uri}");
        }

        connection.Close();
        return false;
    }

    /// <summary>
    /// Send JoinRequest to host (client only).
    /// </summary>
    private void SendJoinRequest()
    {
        if (HostConnection == null)
        {
            LumoraLogger.Error("SendJoinRequest: No host connection");
            return;
        }

        var identity = Lumora.Core.Security.MachineIdentity.Local;
        var cdn = Engine.Current?.CDNClient;
        bool hasAccount = cdn != null && cdn.HasAccountIdentity;

        // Our challenge to the host: it must sign this so we know we're talking to the real host and not a
        // man in the middle. We hold onto it to check the host's signature in HandleJoinChallenge. -xlinka
        var hostVerificationToken = RandomNumberGenerator.GetBytes(32);
        _hostVerificationToken = hostVerificationToken;

        var requestData = new LegacyJoinRequestData
        {
            UserName = Environment.MachineName,
            // MachineID is now the self-certifying hash of our public key, not a free-text string. We
            // prove we hold the matching private key when the host challenges us. -xlinka
            MachineID = identity.MachineId,
            MachinePublicKey = identity.PublicKey,
            UserID = "",
            HeadDevice = (byte)(Engine.Current?.InputInterface?.CurrentHeadOutputDevice ?? HeadOutputDevice.Screen),
            // If we're signed into an account, name it + the login session so the host can fetch our
            // cloud-published key and verify we own it. Empty = guest (machine key only). -xlinka
            AccountUserId = hasAccount ? cdn!.AccountUserId! : "",
            AccountSessionId = hasAccount ? cdn!.AccountSessionId! : "",
            HostVerificationToken = hostVerificationToken
        };

        var controlMessage = new ControlMessage(ControlMessage.Message.JoinRequest)
        {
            Payload = requestData.Encode()
        };

        byte[] encoded = controlMessage.Encode();
        HostConnection.Send(encoded, encoded.Length, reliable: true, background: false);

        LumoraLogger.Log($"Sent JoinRequest to host - UserName='{requestData.UserName}'");
    }

    private void OnPeerConnected(IConnection peer)
    {
        var ip = GetIpKey(peer);

        // Cap both total pending connections and pending per source IP. Without
        // this, a single attacker can open thousands of LNL connections that sit
        // forever before sending JoinRequest and exhaust host memory/sockets.
        bool admit;
        lock (_lock)
        {
            // Count connections still mid-handshake (waiting to authenticate) too, otherwise an attacker
            // could send a JoinRequest, sit in _pendingAuth, free the pending slot, and repeat. -xlinka
            if (_pendingConnections.Count + _pendingAuth.Count >= NetworkLimits.MaxPendingConnections)
            {
                admit = false;
            }
            else if (_pendingByIp.TryGetValue(ip, out var perIp) && perIp >= NetworkLimits.MaxPendingPerIP)
            {
                admit = false;
            }
            else
            {
                admit = true;
                _pendingConnections.Add(peer);
                _pendingByIp[ip] = _pendingByIp.TryGetValue(ip, out var n) ? n + 1 : 1;
            }
        }

        if (!admit)
        {
            LumoraLogger.Warn($"Peer {peer.Identifier} from {ip} rejected: pending-connection limit reached.");
            peer.Close();
            return;
        }

        LumoraLogger.Log($"Peer connected: {peer.Identifier}");
        peer.DataReceived += (data, length) => OnConnectionDataReceived(peer, data, length);
        LumoraLogger.Log($"Peer {peer.Identifier} added to pending - waiting for JoinRequest");
    }

    private void OnPeerDisconnected(IConnection peer)
    {
        LumoraLogger.Log($"Peer disconnected: {peer.Identifier}");

        Session.AssetTransferer?.ConnectionClosed(peer);

        lock (_lock)
        {
            RemovePendingLocked(peer);
            _pendingAuth.Remove(peer); // drop any half-finished handshake so it can't leak. -xlinka
            if (_connectionToUser.TryGetValue(peer, out var user))
            {
                _connectionToUser.Remove(peer);
                _userToConnection.Remove(user);
                RemoveUser(user);
            }
        }
    }

    /// <summary>Remove a connection from the pending set and decrement its per-IP count. Caller must hold _lock.</summary>
    private bool RemovePendingLocked(IConnection connection)
    {
        if (!_pendingConnections.Remove(connection))
            return false;

        var ip = GetIpKey(connection);
        if (_pendingByIp.TryGetValue(ip, out var n))
        {
            if (n <= 1) _pendingByIp.Remove(ip);
            else _pendingByIp[ip] = n - 1;
        }
        return true;
    }

    private static string GetIpKey(IConnection connection)
        => connection?.IP?.ToString() ?? connection?.Identifier ?? "<unknown>";

    /// <summary>
    /// Handle incoming JoinRequest from client.
    /// </summary>
    public void HandleJoinRequest(IConnection connection, LegacyJoinRequestData requestData)
    {
        bool isPending;
        lock (_lock)
        {
            isPending = RemovePendingLocked(connection);
        }

        if (!isPending)
        {
            LumoraLogger.Warn($"HandleJoinRequest: Connection {connection.Identifier} was not pending");
            SendJoinReject(connection, "Duplicate or stale join request");
            return;
        }

        LumoraLogger.Log($"HandleJoinRequest: Received from {connection.Identifier} - UserName='{requestData.UserName}'");
        var rejectReason = ValidateJoinRequest(connection, requestData);
        if (rejectReason != null)
        {
            LumoraLogger.Warn($"HandleJoinRequest: Rejected {connection.Identifier} - {rejectReason}");
            SendJoinReject(connection, rejectReason);
            return;
        }

        // The request checks out (machine id matches its key, not banned, room to join). Don't grant yet:
        // make them prove they actually hold the private key by signing a fresh random nonce. Only after
        // that signature verifies do we mint a User. This is what stops someone just claiming an id. -xlinka
        var nonce = RandomNumberGenerator.GetBytes(32);
        lock (_lock)
        {
            _pendingAuth[connection] = new PendingJoin { Request = requestData, Nonce = nonce };
        }
        SendJoinChallenge(connection, nonce, requestData.HostVerificationToken);
    }

    private string? ValidateJoinRequest(IConnection connection, LegacyJoinRequestData requestData)
    {
        if (connection == null || !connection.IsOpen)
            return "Connection is not open";

        if (World == null || World.IsDisposed || World.IsDestroyed)
            return "World is not available";

        if (!World.IsAuthority)
            return "Only the authority can approve joins";

        if (!(World.Configuration?.AllowJoin.Value ?? true))
            return "This world is not accepting joins";

        var maxUsers = GetEffectiveMaxUsers();
        if (World.UserCount >= maxUsers)
            return $"Session is full ({World.UserCount}/{maxUsers})";

        if (!World.RefIDAllocator.CanAllocateMoreUsers)
            return "Session has no remaining user allocation slots";

        if (!string.IsNullOrWhiteSpace(requestData.UserName) && requestData.UserName.Length > 32)
            return "Username is too long";

        if (!string.IsNullOrWhiteSpace(requestData.MachineID) && requestData.MachineID.Length > 128)
            return "Machine identifier is too long";

        // The MachineID has to be the self-certifying hash of the public key the client sent. Ownership
        // of the matching PRIVATE key is proven a step later via the signed challenge; this just rejects a
        // mismatched/forged id up front, so the ban check below keys on something that can't be faked. -xlinka
        if (requestData.MachinePublicKey == null || requestData.MachinePublicKey.Length == 0)
            return "Missing machine public key";
        if (!Lumora.Core.Security.MachineIdentity.IsMachineIdForKey(requestData.MachineID, requestData.MachinePublicKey))
            return "Machine identity does not match its key";

        // Bounce banned identities HERE, before we mint a User or sync anything. The ban check used to
        // only fire later in AddUser, by which point the user was already in the collection and synced,
        // so a banned user ended up half-joined with the connection still open. This is the gate, so the
        // ban belongs at the gate. Caveat: it's only as strong as what it keys on, and right now MachineID
        // is client-supplied and the account UserID isn't verified, so a determined evader can still spoof
        // past it until we have real join verification. Still beats leaving the door open. -xlinka
        if (Lumora.Core.Security.BanManager.IsBanned(requestData.UserID, requestData.MachineID, World.WorldName?.Value))
            return "You are banned from this world";

        if (!string.IsNullOrWhiteSpace(requestData.UserID))
        {
            lock (_lock)
            {
                if (_connectionToUser.Values.Any(u => u.UserID?.Value == requestData.UserID))
                    return "User is already connected";
            }
        }

        return null;
    }

    private int GetEffectiveMaxUsers()
    {
        var metadataMax = Session?.Metadata?.MaxUsers ?? 0;
        var configMax = World?.Configuration?.MaxUsers.Value ?? 0;
        var allocatorMax = World?.RefIDAllocator?.MaxUserCount ?? 1;
        var requestedMax = metadataMax > 0 ? metadataMax : configMax;
        return global::System.Math.Clamp(requestedMax > 0 ? requestedMax : allocatorMax, 1, allocatorMax);
    }

    private void SendJoinReject(IConnection connection, string reason)
    {
        if (connection == null)
            return;

        var rejectData = new LegacyJoinRejectData
        {
            Reason = string.IsNullOrWhiteSpace(reason) ? "Join rejected" : reason
        };

        var controlMessage = new ControlMessage(ControlMessage.Message.JoinReject)
        {
            Payload = rejectData.Encode()
        };

        var encoded = controlMessage.Encode();
        connection.Send(encoded, encoded.Length, reliable: true, background: false);

        _ = Task.Run(async () =>
        {
            await Task.Delay(250);
            connection.Close();
        });
    }

    private void SendJoinGrant(IConnection connection, string userName, string machineId, string accountId = "", bool forceSilenced = false)
    {
        if (!World.IsAuthority)
        {
            LumoraLogger.Error("Only authority can send JoinGrant");
            return;
        }

        // Allocate ID range using RefIDAllocator
        var (allocStart, allocEnd) = World.RefIDAllocator.AllocateUserIDRange();

        // Use the start of the range as the user's RefID
        RefID userRefID = allocStart;
        ulong userID = (ulong)userRefID;

        var grantData = new LegacyJoinGrantData
        {
            AssignedUserID = userID,
            AllocationIDStart = (ulong)allocStart,
            AllocationIDEnd = (ulong)allocEnd,
            MaxUsers = GetEffectiveMaxUsers(),
            WorldTime = World.TotalTime,
            StateVersion = World.StateVersion
        };

        var controlMessage = new ControlMessage(ControlMessage.Message.JoinGrant)
        {
            Payload = grantData.Encode()
        };

        byte[] encoded = controlMessage.Encode();
        connection.Send(encoded, encoded.Length, reliable: true, background: false);

        LumoraLogger.Log($"Sent JoinGrant to {connection.Identifier} - UserID: {userID}, UserName: '{userName}'");

        // Create user instance, populate fields, then add to the user collection for initialization
        var user = new User();
        user.UserID.Value = userID.ToString();
        user.UserName.Value = !string.IsNullOrEmpty(userName) ? userName : $"Guest {userRefID.GetUserByte()}";
        user.MachineID.Value = machineId ?? "";
        user.AccountId.Value = accountId ?? ""; // verified platform account id, empty for guests. -xlinka
        user.IsSilenced.Value = forceSilenced; // platform mute/spectator ban forces silence on entry. -xlinka
        user.AllocationIDStart.Value = (ulong)allocStart;
        user.AllocationIDEnd.Value = (ulong)allocEnd;
        user.AllocationID.Value = userRefID.GetUserByte();
        user.IsPresent.Value = true;
        user.PresentInWorld.Value = true;

        lock (_lock)
        {
            _connectionToUser[connection] = user;
            _userToConnection[user] = connection;
        }
        LumoraLogger.Log($"SendJoinGrant: Added connection-user mapping for '{userName}'");

        World.AddUserToCollection(user, userRefID, isNewlyCreated: true);
        LumoraLogger.Log($"SendJoinGrant: User '{userName}' added to world");

        if (Session.Sync != null)
        {
            LumoraLogger.Log($"SendJoinGrant: Calling QueueUserForInitialization for '{userName}'");
            Session.Sync.QueueUserForInitialization(user);
        }
        else
        {
            LumoraLogger.Error("SendJoinGrant: Session.Sync is NULL - cannot queue user for initialization!");
        }
    }

    /// <summary>
    /// Host -> joiner: send the random nonce the joiner must sign with its machine key to prove identity.
    /// </summary>
    private void SendJoinChallenge(IConnection connection, byte[] nonce, byte[] hostVerificationToken)
    {
        var hostIdentity = Lumora.Core.Security.MachineIdentity.Local;

        // Prove WE are the real host by signing the joiner's token with our machine key. The joiner checks
        // this before handing over its own signature, so a man in the middle can't pose as the host. -xlinka
        byte[] hostSignature = Array.Empty<byte>();
        if (hostVerificationToken != null && hostVerificationToken.Length > 0)
        {
            try
            {
                hostSignature = hostIdentity.SignChallenge(
                    Lumora.Core.Security.MachineIdentity.BuildJoinPayload(hostVerificationToken, JoinContextHost));
            }
            catch (Exception ex) { LumoraLogger.Warn($"SendJoinChallenge: host self-sign failed ({ex.Message})"); }
        }

        var challenge = new LegacyJoinChallengeData
        {
            Nonce = nonce,
            HostMachineId = hostIdentity.MachineId,
            HostMachinePublicKey = hostIdentity.PublicKey,
            HostMachineSignature = hostSignature
        };
        var controlMessage = new ControlMessage(ControlMessage.Message.JoinChallenge)
        {
            Payload = challenge.Encode()
        };

        byte[] encoded = controlMessage.Encode();
        connection.Send(encoded, encoded.Length, reliable: true, background: false);
        LumoraLogger.Log($"Sent JoinChallenge to {connection.Identifier}");
    }

    /// <summary>
    /// Client side: the host challenged us, so sign the nonce with our machine key and send it back.
    /// </summary>
    public void HandleJoinChallenge(IConnection connection, LegacyJoinChallengeData challenge)
    {
        if (challenge.Nonce == null || challenge.Nonce.Length == 0)
        {
            LumoraLogger.Warn("HandleJoinChallenge: empty nonce, ignoring");
            return;
        }

        // Verify the HOST first: its MachineId has to hash to the key it sent, and it has to have signed
        // the token we put in our JoinRequest. If that doesn't check out we're talking to an impostor or a
        // man in the middle, so bail without ever handing over our own signature. -xlinka
        var token = _hostVerificationToken;
        bool hostOk =
            token != null && token.Length > 0
            && Lumora.Core.Security.MachineIdentity.IsMachineIdForKey(challenge.HostMachineId, challenge.HostMachinePublicKey)
            && Lumora.Core.Security.MachineIdentity.VerifyRsaSignature(
                challenge.HostMachinePublicKey,
                Lumora.Core.Security.MachineIdentity.BuildJoinPayload(token, JoinContextHost),
                challenge.HostMachineSignature);

        if (!hostOk)
        {
            LumoraLogger.Error("HandleJoinChallenge: host identity verification FAILED - aborting join (possible MITM)");
            (connection ?? HostConnection)?.Close();
            _hostVerificationToken = null;
            return;
        }

        byte[] signature;
        try
        {
            signature = Lumora.Core.Security.MachineIdentity.Local.SignChallenge(
                Lumora.Core.Security.MachineIdentity.BuildJoinPayload(challenge.Nonce, JoinContextUserMachine));
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"HandleJoinChallenge: failed to sign challenge ({ex.Message})");
            return;
        }

        // If signed into an account, also sign the nonce (account context) with our account key so the host
        // can verify our account against the key we published to the cloud. Best-effort, guests skip. -xlinka
        byte[] accountSignature = Array.Empty<byte>();
        var cdn = Engine.Current?.CDNClient;
        if (cdn != null && cdn.HasAccountIdentity)
        {
            try
            {
                accountSignature = cdn.SignWithAccountKey(
                    Lumora.Core.Security.MachineIdentity.BuildJoinPayload(challenge.Nonce, JoinContextUserAccount)) ?? Array.Empty<byte>();
            }
            catch (Exception ex) { LumoraLogger.Warn($"HandleJoinChallenge: account sign failed ({ex.Message})"); }
        }

        var auth = new LegacyJoinAuthenticateData { Signature = signature, AccountSignature = accountSignature };
        var controlMessage = new ControlMessage(ControlMessage.Message.JoinAuthenticate)
        {
            Payload = auth.Encode()
        };

        byte[] encoded = controlMessage.Encode();
        var target = connection ?? HostConnection;
        target.Send(encoded, encoded.Length, reliable: true, background: false);
        _hostVerificationToken = null; // one-shot; done with this join's host challenge. -xlinka
        LumoraLogger.Log("Sent JoinAuthenticate to host");
    }

    /// <summary>
    /// Host side: verify the joiner's signed challenge. Only if the signature proves they hold the
    /// private key behind their claimed MachineID do we actually grant the join. Otherwise reject. -xlinka
    /// </summary>
    public async Task HandleJoinAuthenticate(IConnection connection, LegacyJoinAuthenticateData auth)
    {
        if (connection == null || !World.IsAuthority)
            return;

        PendingJoin? pending = null;
        lock (_lock)
        {
            _pendingAuth.TryGetValue(connection, out pending);
            if (pending != null)
                _pendingAuth.Remove(connection);
        }

        if (pending == null)
        {
            LumoraLogger.Warn($"HandleJoinAuthenticate: no pending auth for {connection.Identifier} - ignoring");
            SendJoinReject(connection, "No pending authentication");
            return;
        }

        try
        {
            // 1. The machine key is always required: prove ownership of the claimed MachineID.
            if (!Lumora.Core.Security.MachineIdentity.VerifyChallenge(
                    pending.Request.MachineID, pending.Request.MachinePublicKey,
                    Lumora.Core.Security.MachineIdentity.BuildJoinPayload(pending.Nonce, JoinContextUserMachine),
                    auth.Signature))
            {
                LumoraLogger.Warn($"HandleJoinAuthenticate: machine signature FAILED for {connection.Identifier} - rejecting");
                SendJoinReject(connection, "Machine authentication failed");
                return;
            }

            // 2. The account check only runs when the joiner CLAIMS an account. We fail closed: if
            // they claim one and we can't verify it (no key published, bad signature, platform-banned, or
            // the backend is unreachable), reject. A guest just doesn't claim an account. -xlinka
            var claimedAccount = pending.Request.AccountUserId;
            if (!string.IsNullOrEmpty(claimedAccount))
            {
                var verified = await VerifyAccountAsync(
                    claimedAccount, pending.Request.AccountSessionId, pending.Nonce, auth.AccountSignature);

                if (verified == null)
                {
                    LumoraLogger.Warn($"HandleJoinAuthenticate: account verification FAILED for {connection.Identifier} (claimed '{claimedAccount}') - rejecting");
                    SendJoinReject(connection, "Account verification failed");
                    return;
                }

                var (verifiedAccountId, forceSilenced) = verified.Value;

                // We're on a threadpool continuation after the HTTP await, so marshal the grant (which
                // mutates world state: creates + registers the User) back onto the world's sync thread.
                // The guest path below skips this because it never awaited and is still on-thread. -xlinka
                var world = World;
                if (world == null)
                {
                    SendJoinReject(connection, "World no longer available");
                    return;
                }
                var conn = connection;
                var req = pending.Request;
                LumoraLogger.Log($"HandleJoinAuthenticate: {connection.Identifier} authenticated (account='{verifiedAccountId}', silenced={forceSilenced}) - granting");
                world.RunSynchronously(() => SendJoinGrant(conn, req.UserName, req.MachineID, verifiedAccountId, forceSilenced));
                return;
            }

            // Guest path: no await happened, so we're still on the sync/message thread, grant inline. -xlinka
            LumoraLogger.Log($"HandleJoinAuthenticate: {connection.Identifier} authenticated (guest) - granting");
            SendJoinGrant(connection, pending.Request.UserName, pending.Request.MachineID, "");
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"HandleJoinAuthenticate: unexpected error for {connection.Identifier} ({ex.Message}) - rejecting");
            SendJoinReject(connection, "Authentication error");
        }
    }

    /// <summary>
    /// Verify a joiner's CLAIMED account: fetch the public key that account published to the cloud for the
    /// given login session, verify the account signature over the challenge nonce against THAT key (so the
    /// joiner can't present a key of their choosing), then check platform moderation ban status. Returns
    /// the verified account id, or null if anything fails. World bans are a separate, host-side check. -xlinka
    /// </summary>
    private async Task<(string accountId, bool forceSilenced)?> VerifyAccountAsync(string accountUserId, string accountSessionId, byte[] nonce, byte[] accountSignature)
    {
        var cdn = Engine.Current?.CDNClient;
        if (cdn == null)
        {
            LumoraLogger.Warn("VerifyAccount: no CDN client available; cannot verify a claimed account.");
            return null;
        }
        if (string.IsNullOrEmpty(accountSessionId) || accountSignature == null || accountSignature.Length == 0)
            return null;

        var publishedKey = await cdn.GetSessionPublicKeyAsync(accountUserId, accountSessionId);
        if (publishedKey == null)
        {
            LumoraLogger.Warn($"VerifyAccount: no published key for {accountUserId}/{accountSessionId}.");
            return null;
        }

        if (!Lumora.Core.Security.MachineIdentity.VerifyRsaSignature(
                publishedKey,
                Lumora.Core.Security.MachineIdentity.BuildJoinPayload(nonce, JoinContextUserAccount),
                accountSignature))
        {
            LumoraLogger.Warn($"VerifyAccount: account signature did not verify for {accountUserId}.");
            return null;
        }

        // Platform moderation bans. Public/account ban blocks the join outright. Mute/spectator are softer:
        // they restrict rather than block. We force-silence a mute-banned user; spectator-ban should force
        // spectator-only, but there's no spectator mode yet, so we silence + flag the gap. -xlinka
        bool forceSilenced = false;
        var userInfo = await cdn.GetPlatformUserAsync(accountUserId);
        if (userInfo.Success && userInfo.Data != null)
        {
            var info = userInfo.Data;
            if (info.IsPublicBanned || info.IsAccountBanned)
            {
                LumoraLogger.Warn($"VerifyAccount: {accountUserId} is platform-banned; rejecting.");
                return null;
            }
            if (info.IsMuteBanned)
                forceSilenced = true;
            if (info.IsSpectatorBanned)
            {
                forceSilenced = true;
                LumoraLogger.Warn($"VerifyAccount: {accountUserId} is spectator-banned, but there's no spectator mode yet, so silencing as a partial measure. -xlinka");
            }
        }

        return (accountUserId, forceSilenced);
    }

    private void RemoveUser(User user)
    {
        World.RemoveUser(user);
        user.Dispose();
    }

    private void OnConnectionDataReceived(IConnection connection, byte[] data, int length)
    {
        if (Session?.Sync == null)
            return;

        var raw = new RawInMessage
        {
            Data = data,
            Offset = 0,
            Length = length,
            Sender = connection
        };

        Session.Sync.QueueRawIncoming(raw);
    }

    /// <summary>
    /// Get user for connection.
    /// </summary>
    public bool TryGetUser(IConnection connection, out User user)
    {
        lock (_lock)
        {
            if (_connectionToUser.TryGetValue(connection, out var found))
            {
                user = found;
                return true;
            }
            user = default!;
            return false;
        }
    }

    /// <summary>
    /// Get connection for user.
    /// </summary>
    public bool TryGetConnection(User user, out IConnection connection)
    {
        lock (_lock)
        {
            if (_userToConnection.TryGetValue(user, out var found))
            {
                connection = found;
                return true;
            }
            connection = default!;
            return false;
        }
    }

    /// <summary>
    /// Get all connections for broadcasting.
    /// </summary>
    public List<IConnection> GetAllConnections()
    {
        lock (_lock)
        {
            return _connectionToUser.Keys.ToList();
        }
    }

    /// <summary>
    /// Broadcast data to specified connections.
    /// </summary>
    public void Broadcast(byte[] data, List<IConnection> targets, bool reliable)
    {
        foreach (var target in targets)
        {
            target.Send(data, data.Length, reliable, background: false);
        }
    }

    /// <summary>
    /// Poll network events for this session's connections and listeners.
    /// Must be called every frame by the world update loop.
    /// </summary>
    public void Poll()
    {
        // Per-session poll. Most transports also have a global poll driven by
        // NetworkManagerRegistry.UpdateAll() from the engine update loop, which
        // covers any listeners/connections created outside the session. - xlinka
        if (Listener is LNL.LNLListener lnlListener) lnlListener.Poll();
        HostConnection?.Poll();
    }

    public void Dispose()
    {
        Listener?.Close();
        Listener?.Dispose();
        HostConnection?.Close();

        lock (_lock)
        {
            _pendingConnections.Clear();
            _pendingByIp.Clear();
            _connectionToUser.Clear();
            _userToConnection.Clear();
        }
    }
}

