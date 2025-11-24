using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Lumora.Core;
using Lumora.Core.Networking.LNL;
using Lumora.Core.Networking.Messages;
using Lumora.Core.Networking.Sync;
using AquaLogger = Lumora.Core.Logging.Logger;
using StreamType = Lumora.Core.Networking.Sync.StreamType;

namespace Lumora.Core.Networking.Session;

/// <summary>
/// Manages synchronization with dedicated sync thread.
/// </summary>
public class SessionSyncManager : IDisposable
{
    private Thread _syncThread;
    private bool _running;
    private readonly AutoResetEvent _worldUpdateFinished = new(false);
    private readonly object _lock = new();

    public Session Session { get; private set; }
    public World World => Session?.World;

    // Sync rate in Hz
    public int SyncRate { get; set; } = 20;

    public SessionSyncManager(Session session)
    {
        Session = session;
    }

    /// <summary>
    /// Start the sync thread.
    /// </summary>
    public void Start()
    {
        if (_syncThread != null)
        {
            throw new InvalidOperationException("Sync thread already started");
        }

        _running = true;
        _syncThread = new Thread(SyncLoop)
        {
            Name = "SessionSyncThread",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };

        _syncThread.Start();
        AquaLogger.Log("Sync thread started");
    }

    /// <summary>
    /// Main sync loop running on dedicated thread.
    /// </summary>
    private void SyncLoop()
    {
        World.HookManager.DataModelLock(Thread.CurrentThread);

        ulong lastSyncTime = 0;
        int syncInterval = 1000 / SyncRate;

        while (_running && !Session.IsDisposed)
        {
            try
            {
                // Stage 1: Wait for world update to finish
                _worldUpdateFinished.WaitOne(syncInterval);

                if (!_running) break;

                // Stage 2: Increment sync tick
                World.IncrementSyncTick();

                // Stage 3: Generate and send delta batch (authority only)
                if (World.IsAuthority)
                {
                    GenerateAndSendDeltaBatch();
                }

                // Stage 4: Generate and send streams (high-frequency data)
                GenerateAndSendStreams();

                lastSyncTime++;

                // Sleep to maintain sync rate
                Thread.Sleep(syncInterval);
            }
            catch (Exception ex)
            {
                AquaLogger.Error($"Exception in sync loop: {ex.Message}");
            }
        }

        World.HookManager.DataModelUnlock();
        AquaLogger.Log("Sync thread stopped");
    }

    /// <summary>
    /// Signal that world update has finished.
    /// Called from main thread.
    /// </summary>
    public void SignalWorldUpdateFinished()
    {
        _worldUpdateFinished.Set();
    }

    /// <summary>
    /// Generate delta batch from dirty sync members and send to all clients.
    /// </summary>
    private void GenerateAndSendDeltaBatch()
    {
        var deltaBatch = new DeltaBatch
        {
            SenderStateVersion = World.StateVersion,
            WorldTime = World.TotalTime
        };

        // Collect dirty sync members from all users
        foreach (var user in World.GetAllUsers())
        {
            var dirtyMembers = user.GetDirtySyncMembers();
            foreach (var member in dirtyMembers)
            {
                // Serialize member data
                using var ms = new MemoryStream();
                using var writer = new BinaryWriter(ms);
                member.Encode(writer);

                var record = new DataRecord
                {
                    TargetID = user.ReferenceID,
                    MemberIndex = member.MemberIndex,
                    Data = ms.ToArray()
                };

                deltaBatch.Records.Add(record);
            }

            user.ClearDirtyFlags();
        }

        // Only send if there are changes
        if (deltaBatch.Records.Count > 0)
        {
            byte[] encoded = deltaBatch.Encode();
            Session.Messages.BroadcastMessage(encoded, reliable: true);

            AquaLogger.Debug($"Sent DeltaBatch with {deltaBatch.Records.Count} records");
        }
    }

    /// <summary>
    /// Generate and send stream messages (transforms, audio, etc.).
    /// </summary>
    private void GenerateAndSendStreams()
    {
        var streamMessage = new StreamMessage();

        // Collect stream data from all users
        foreach (var user in World.GetAllUsers())
        {
            if (!user.StreamBag.HasData) continue;

            // Head transform
            if (user.StreamBag.HeadTransform.HasValue)
            {
                streamMessage.Entries.Add(new StreamEntry
                {
                    UserID = user.ReferenceID,
                    StreamID = (int)StreamType.HeadTransform,
                    Data = user.StreamBag.HeadTransform.Value.Encode()
                });
            }

            // Left hand transform
            if (user.StreamBag.LeftHandTransform.HasValue)
            {
                streamMessage.Entries.Add(new StreamEntry
                {
                    UserID = user.ReferenceID,
                    StreamID = (int)StreamType.LeftHandTransform,
                    Data = user.StreamBag.LeftHandTransform.Value.Encode()
                });
            }

            // Right hand transform
            if (user.StreamBag.RightHandTransform.HasValue)
            {
                streamMessage.Entries.Add(new StreamEntry
                {
                    UserID = user.ReferenceID,
                    StreamID = (int)StreamType.RightHandTransform,
                    Data = user.StreamBag.RightHandTransform.Value.Encode()
                });
            }

            // Clear stream bag after encoding
            user.StreamBag.Clear();
        }

        // Send if there's stream data
        if (streamMessage.Entries.Count > 0)
        {
            byte[] encoded = streamMessage.Encode();
            Session.Messages.BroadcastMessage(encoded, reliable: false); // Streams are unreliable for low latency

            AquaLogger.Debug($"Sent StreamMessage with {streamMessage.Entries.Count} entries");
        }
    }

    /// <summary>
    /// Process incoming delta batch from a client.
    /// </summary>
    public void ProcessDeltaBatch(IConnection connection, DeltaBatch delta)
    {
        lock (_lock)
        {
            AquaLogger.Debug($"Processing DeltaBatch with {delta.Records.Count} records from {connection.Identifier}");

            // If we're authority, validate and confirm/correct changes
            if (World.IsAuthority)
            {
                var confirmation = new ConfirmationMessage
                {
                    ClientStateVersion = delta.SenderStateVersion,
                    AuthorityStateVersion = World.StateVersion
                };

                foreach (var record in delta.Records)
                {
                    var validation = ValidateDataRecord(record, connection);

                    if (validation.IsValid)
                    {
                        // Accept: apply change and increment state version
                        ApplyDataRecord(record);
                        World.IncrementStateVersion();

                        confirmation.Records.Add(new ConfirmationRecord
                        {
                            TargetID = record.TargetID,
                            MemberIndex = record.MemberIndex,
                            Accepted = true
                        });
                    }
                    else
                    {
                        // Reject: send correction
                        AquaLogger.Warn($"Rejected delta change: {validation.RejectionReason}");

                        byte[] correctedData = null;
                        if (validation.CorrectedValue != null)
                        {
                            // TODO: Platform-agnostic serialization needed
                            // correctedData = Serializer.Serialize(validation.CorrectedValue);
                            AquaLogger.Warn("Corrected value serialization not yet implemented");
                        }

                        confirmation.Records.Add(new ConfirmationRecord
                        {
                            TargetID = record.TargetID,
                            MemberIndex = record.MemberIndex,
                            Accepted = false,
                            CorrectedData = correctedData,
                            RejectionReason = validation.RejectionReason
                        });
                    }
                }

                // Update confirmation with new authority state version
                confirmation.AuthorityStateVersion = World.StateVersion;

                // Send confirmation back to client
                byte[] confirmationData = confirmation.Encode();
                Session.Messages.SendToConnection(connection, confirmationData, reliable: true);

                // Broadcast accepted changes to other clients
                if (confirmation.Records.Any(r => r.Accepted))
                {
                    byte[] encoded = delta.Encode();
                    Session.Messages.BroadcastMessageExcept(connection, encoded, reliable: true);
                }
            }
            else
            {
                // Clients accept all deltas from authority without validation
                foreach (var record in delta.Records)
                {
                    ApplyDataRecord(record);
                }

                // Update our state version to match authority
                World.SetStateVersion(delta.SenderStateVersion);
            }
        }
    }

    /// <summary>
    /// Process full state batch.
    /// </summary>
    public void ProcessFullBatch(IConnection connection, FullBatch full)
    {
        lock (_lock)
        {
            AquaLogger.Log($"Processing FullBatch with {full.Records.Count} records");

            foreach (var record in full.Records)
            {
                ApplyDataRecord(record);
            }
        }
    }

    /// <summary>
    /// Process stream message.
    /// </summary>
    public void ProcessStreamMessage(IConnection connection, StreamMessage stream)
    {
        lock (_lock)
        {
            foreach (var entry in stream.Entries)
            {
                // Find the user
                var user = World.GetAllUsers().Find(u => u.ReferenceID == entry.UserID);
                if (user == null)
                {
                    AquaLogger.Warn($"Stream entry for unknown user {entry.UserID}");
                    continue;
                }

                // Decode stream data based on type
                StreamType streamType = (StreamType)entry.StreamID;
                switch (streamType)
                {
                    case StreamType.HeadTransform:
                        user.StreamBag.HeadTransform = TransformStreamData.Decode(entry.Data);
                        break;

                    case StreamType.LeftHandTransform:
                        user.StreamBag.LeftHandTransform = TransformStreamData.Decode(entry.Data);
                        break;

                    case StreamType.RightHandTransform:
                        user.StreamBag.RightHandTransform = TransformStreamData.Decode(entry.Data);
                        break;

                    default:
                        AquaLogger.Warn($"Unknown stream type: {streamType}");
                        break;
                }
            }

            AquaLogger.Debug($"Processed stream message with {stream.Entries.Count} entries");
        }
    }

    /// <summary>
    /// Validate a data record before applying it.
    /// Authority checks if the change is allowed.
    /// </summary>
    private DeltaValidationResult ValidateDataRecord(DataRecord record, IConnection sender)
    {
        try
        {
            var element = World.FindElement(record.TargetID);

            if (element == null)
            {
                return DeltaValidationResult.Reject($"Element {record.TargetID} not found");
            }

            // Check if this is a sync object
            if (element is not ISyncObject syncObject)
            {
                return DeltaValidationResult.Reject($"Element {record.TargetID} is not a sync object");
            }

            // Check if member index is valid
            if (record.MemberIndex < 0 || record.MemberIndex >= syncObject.SyncMembers.Count)
            {
                return DeltaValidationResult.Reject($"Invalid member index {record.MemberIndex}");
            }

            var member = syncObject.SyncMembers[record.MemberIndex];

            // Check authority/ownership
            // For User objects, only the user themselves can modify their own data
            if (element is User user)
            {
                // TODO: Get sender's user ID from connection and verify it matches
                // For now, accept all user changes (will implement proper ownership checking)
                return DeltaValidationResult.Accept();
            }

            // For Slots and Components, check if sender has permission
            // TODO: Implement ownership/permission system
            // For now, accept all changes from any connected client
            return DeltaValidationResult.Accept();
        }
        catch (Exception ex)
        {
            AquaLogger.Error($"Validation error: {ex.Message}");
            return DeltaValidationResult.Reject($"Validation exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply a data record to the world state.
    /// </summary>
    private void ApplyDataRecord(DataRecord record)
    {
        try
        {
            var element = World.FindElement(record.TargetID);
            if (element is ISyncObject syncObject)
            {
                if (record.MemberIndex < syncObject.SyncMembers.Count)
                {
                    var member = syncObject.SyncMembers[record.MemberIndex];

                    using var ms = new MemoryStream(record.Data);
                    using var reader = new BinaryReader(ms);
                    member.Decode(reader);

                    AquaLogger.Debug($"Applied {member.Name} to {element}");
                }
                else
                {
                    AquaLogger.Warn($"Invalid member index {record.MemberIndex} for {element}");
                }
            }
            else
            {
                AquaLogger.Warn($"Element {record.TargetID} not found or not ISyncObject");
            }
        }
        catch (Exception ex)
        {
            AquaLogger.Error($"Failed to apply data record: {ex.Message}");
        }
    }

    /// <summary>
    /// Process confirmation message from authority.
    /// Apply corrections for rejected changes.
    /// </summary>
    public void ProcessConfirmation(IConnection connection, ConfirmationMessage confirmation)
    {
        lock (_lock)
        {
            AquaLogger.Debug($"Processing confirmation with {confirmation.Records.Count} records from authority");

            // Update our state version to match authority
            World.SetStateVersion(confirmation.AuthorityStateVersion);

            foreach (var record in confirmation.Records)
            {
                if (record.Accepted)
                {
                    // Change was accepted, nothing to do
                    AquaLogger.Debug($"Change accepted for element {record.TargetID} member {record.MemberIndex}");
                }
                else
                {
                    // Change was rejected, apply correction
                    AquaLogger.Warn($"Change rejected: {record.RejectionReason}");

                    if (record.CorrectedData != null)
                    {
                        // Apply the corrected value from authority
                        var element = World.FindElement(record.TargetID);
                        if (element is ISyncObject syncObject && record.MemberIndex < syncObject.SyncMembers.Count)
                        {
                            var member = syncObject.SyncMembers[record.MemberIndex];

                            using var ms = new MemoryStream(record.CorrectedData);
                            using var reader = new BinaryReader(ms);
                            member.Decode(reader);

                            AquaLogger.Log($"Applied correction from authority for {member.Name}");
                        }
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        _running = false;
        _worldUpdateFinished.Set(); // Wake up thread

        if (_syncThread != null)
        {
            _syncThread.Join(timeout: TimeSpan.FromSeconds(5));
            _syncThread = null;
        }

        _worldUpdateFinished.Dispose();
    }
}
