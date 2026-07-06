// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Lumora.Core;
using Lumora.Core.Networking.Streams;
using Lumora.Core.Networking.Sync;
using LegacyJoinGrantData = Lumora.Core.Networking.Messages.JoinGrantData;
using LegacyJoinRequestData = Lumora.Core.Networking.Messages.JoinRequestData;
using LegacyJoinRejectData = Lumora.Core.Networking.Messages.JoinRejectData;
using LegacyJoinChallengeData = Lumora.Core.Networking.Messages.JoinChallengeData;
using LegacyJoinAuthenticateData = Lumora.Core.Networking.Messages.JoinAuthenticateData;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking.Session;

/// <summary>
/// Manages synchronization with 3 dedicated threads (decode, encode, sync).
/// </summary>
public class SessionSyncManager : IDisposable
{
    public enum SyncLoopStage
    {
        WaitingForSyncThreadEvent,
        RunningMessageProcessing,
        ExitedMessageProcessing,
        ProcessingStopped,
        ContinuingAfterRefreshFinished,
        GeneratingDeltaBatch,
        GeneratingCorrections,
        EncodingStreams,
        FinishingSyncCycle,
        ProcessingControlMessages,
        InitializingNewUsers,
        Finished
    }

    // Threads
    private Thread _decodeThread = null!;
    private Thread _encodeThread = null!;
    private Thread _syncThread = null!;

    // Thread synchronization
    private readonly AutoResetEvent _decodeThreadEvent = new(false);
    private readonly AutoResetEvent _encodeThreadEvent = new(false);
    private readonly AutoResetEvent _syncThreadEvent = new(false);
    private readonly ManualResetEvent _syncThreadInitEvent = new(false);
    private readonly ManualResetEvent _refreshFinished = new(false);
    private readonly ManualResetEvent _lastProcessingStopped = new(false);

    // Message queues
    private readonly ConcurrentQueue<RawInMessage> _rawInMessages = new();
    private readonly ConcurrentQueue<SyncMessage> _messagesToProcess = new();
    private readonly ConcurrentQueue<SyncMessage> _messagesToTransmit = new();
    private readonly Queue<DeltaBatch> _pendingDeltaBatches = new();

    // Reused every sync cycle for the released-drive corrections and stream gathering so we don't
    // allocate a fresh list per tick. SYNC-THREAD ONLY: safe only while the sync cycle runs serially on
    // one thread - if encode is ever parallelized, re-audit these for aliasing. -xlinka
    private readonly List<SyncElement> _releasedDrivesScratch = new();
    private readonly List<StreamMessage> _streamsScratch = new();
    private readonly Queue<StreamMessage> _pendingStreamMessages = new();
    private const int MaxPendingDeltaBatches = 256;
    private const int MaxJoinPendingDeltaBatches = 16384;   // never drop reliable deltas while joining
    private const int MaxPendingStreamMessages = 512;

    // State
    private bool _running;
    private bool _isDisposed;
    private bool _acceptDeltas;

    // Progress tracking for client initialization
    private int _expectedComponents = 0;
    private int _receivedComponents = 0;
    private int _initializedComponents = 0;
    private readonly HashSet<RefID> _receivedComponentIds = new();
    private readonly object _progressLock = new();
    private bool _initialFullBatchReceived = false;

    // Initial-state completeness gate: the client only enters the world
    // (State -> Running) once the initial full batch has FULLY decoded - instead of flipping the
    // moment the JoinStartDelta control message lands and silently dropping whatever hadn't decoded
    // yet. We remember that JoinStartDelta arrived, keep retrying pending records, and transition the
    // instant nothing is left unapplied. If it genuinely gets stuck (an orphaned field record whose
    // owning slot/component never materialized) we log EXACTLY what's missing and enter anyway, so a
    // join can't silently brick - it either lands a complete world or tells you precisely what it
    // couldn't decode. -xlinka
    private bool _joinStartDeltaSeen = false;
    private int _lastInitialPendingCount = -1;
    private ulong _lastInitialProgressTick = 0;
    private bool _loggedInitialIncomplete = false;
    private int _fullStateRequestBudget = 1;            // re-request the full state this many times if stuck
    private const int InitialStateMaxAgeTicks = 2000;   // retry far longer than the live delta path
    private const int InitialStateStuckTicks = 120;     // no progress this long -> recover, then enter anyway
                                                        // (kept short so a stuck join becomes usable in
                                                        // a couple seconds instead of stalling for ~30)

    // Client-side change confirmations (sync tick -> RefIDs)
    private readonly Dictionary<ulong, HashSet<RefID>> _changesToConfirm = new();

    // Pending data records that couldn't decode yet (missing elements)
    private readonly Dictionary<RefID, PendingRecord> _pendingFullRecords = new();
    private readonly Dictionary<RefID, PendingRecord> _pendingDeltaRecords = new();
    private readonly object _pendingLock = new();
    private const int PendingRecordMaxAttempts = 20;
    private const int PendingRecordMaxAgeTicks = 400;
    // New user initialization queue
    private readonly List<User> _newUsersToInitialize = new();
    private readonly object _newUsersLock = new();

    // Local user pending initialization (wait for sync-created User)
    internal RefID LocalUserRefIDToInit { get; private set; }
    private string? _pendingLocalUserName;
    private ulong _pendingAllocationStart;
    private ulong _pendingAllocationEnd;

    // Debug
    public SyncLoopStage DEBUG_SyncLoopStage { get; private set; }
    public SyncMessage ProcessingSyncMessage { get; private set; } = null!;

    // Statistics
    public int TotalProcessedMessages { get; private set; }
    public int TotalReceivedDeltas { get; private set; }
    public int TotalReceivedFulls { get; private set; }
    public int TotalReceivedStreams { get; private set; }
    public int TotalSentDeltas { get; private set; }
    public int TotalSentFulls { get; private set; }
    public int TotalSentStreams { get; private set; }
    public int TotalReceivedRawFrames { get; private set; }
    public int TotalSentRawFrames { get; private set; }
    public int TotalCorrections { get; private set; }
    public int LastGeneratedDeltaChanges { get; private set; }

    // Live queue depths for diagnostics (the Network debug tab). Reads of ConcurrentQueue.Count are safe
    // from any thread; the plain Queue count is a benign racy read used for display only.
    public int MessagesToProcessCount => _messagesToProcess.Count;
    public int MessagesToTransmitCount => _messagesToTransmit.Count;
    public int IncomingRawCount => _rawInMessages.Count;
    public int PendingStreamCount => _pendingStreamMessages.Count;

    public Session Session { get; private set; }
    public World World => (Session?.World) ?? null!;

    // Sync send/process rate in Hz, sourced live from user settings so changing the tick rate applies to
    // the running session immediately (the sync loop reads this each idle wait). Clamped 10-120 there. -xlinka
    public int SyncRate => EngineSettings.NetworkTickRate;

    public SessionSyncManager(Session session)
    {
        Session = session;
    }

    /// <summary>
    /// Start all sync threads.
    /// </summary>
    public void Start()
    {
        if (_syncThread != null)
            throw new InvalidOperationException("Sync threads already started");

        _running = true;

        _decodeThread = new Thread(DecodeLoop)
        {
            Name = "SessionDecodeThread",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };

        _encodeThread = new Thread(EncodeLoop)
        {
            Name = "SessionEncodeThread",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };

        _syncThread = new Thread(SyncLoop)
        {
            Name = "SessionSyncThread",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };

        _decodeThread.Start();
        _encodeThread.Start();
        _syncThread.Start();

        // Wait for sync thread to initialize - BOUNDED so a stuck/failed sync thread can never permanently block
        // the caller (the join handshake). The sync thread sets this almost immediately at the top of SyncLoop;
        // 10s is purely a safety net. -xlinka
        if (!_syncThreadInitEvent.WaitOne(10000))
        {
            LumoraLogger.Error("[lnl] SessionSyncManager: sync thread did not initialize within 10s - join may fail");
        }

        LumoraLogger.Log("[lnl] SessionSyncManager: All threads started");
    }

    /// <summary>
    /// Signal that world update has finished.
    /// Called from main thread after World.Update().
    /// Allows the sync thread to advance past the refresh wait.
    /// </summary>
    public void SignalRefreshFinished()
    {
        _refreshFinished.Set();
    }

    /// <summary>
    /// Stop sync processing and wait for main thread refresh.
    /// Sync thread pauses here while the main thread runs World.Update().
    /// </summary>
    public ManualResetEvent ProcessingStopped => _lastProcessingStopped;

    /// <summary>
    /// Event signaling that world refresh has completed.
    /// </summary>
    public WaitHandle RefreshFinished => _refreshFinished;

    /// <summary>
    /// Request the sync thread to stop processing and wait for world refresh.
    /// </summary>
    public void StopProcessing()
    {
        _syncThreadEvent.Set();
    }

    /// <summary>
    /// Queue raw incoming data for decoding.
    /// </summary>
    public void QueueRawIncoming(RawInMessage message)
    {
        _rawInMessages.Enqueue(message);
        _decodeThreadEvent.Set();
    }

    /// <summary>
    /// Queue message for transmission.
    /// </summary>
    public void EnqueueForTransmission(SyncMessage message)
    {
        _messagesToTransmit.Enqueue(message);
        _encodeThreadEvent.Set();
    }

    /// <summary>
    /// Queue message for processing.
    /// </summary>
    private void EnqueueForProcessing(SyncMessage message)
    {
        _messagesToProcess.Enqueue(message);
        _syncThreadEvent.Set();
    }

    /// <summary>
    /// Add user to initialization queue.
    /// </summary>
    public void QueueUserForInitialization(User user)
    {
        LumoraLogger.Log($"[lnl] QueueUserForInitialization: Queuing user '{user.UserName.Value}' (RefID: {user.ReferenceID})");
        // Hold off all live fan-out to this user until its full state is on the wire (re-enabled once
        // initialization finishes). The flag already defaults false, but a re-init of an existing user needs the reset. -xlinka
        user.StopTransmittingStreamData();
        lock (_newUsersLock)
        {
            _newUsersToInitialize.Add(user);
            LumoraLogger.Log($"[lnl] QueueUserForInitialization: Queue now has {_newUsersToInitialize.Count} users");
        }
    }

    // DECODE THREAD

    private void DecodeLoop()
    {
        LumoraLogger.Log("[lnl] DecodeLoop started");

        while (_running && !_isDisposed)
        {
            _decodeThreadEvent.WaitOne();

            if (_isDisposed) break;

            while (_rawInMessages.TryDequeue(out var rawMessage))
            {
                try
                {
                    var syncMessage = SyncMessage.Decode(rawMessage);

                    // Update statistics
                    switch (syncMessage)
                    {
                        case DeltaBatch: TotalReceivedDeltas++; break;
                        case FullBatch: TotalReceivedFulls++; break;
                        case StreamMessage: TotalReceivedStreams++; break;
                        case RawFrameMessage: TotalReceivedRawFrames++; break;
                    }

                    EnqueueForProcessing(syncMessage);
                }
                catch (Exception ex)
                {
                    LumoraLogger.Error($"[lnl] DecodeLoop: Exception decoding message: {ex.Message}");
                }
            }
        }

        LumoraLogger.Log("[lnl] DecodeLoop stopped");
    }

    // ENCODE THREAD

    private void EncodeLoop()
    {
        LumoraLogger.Log("[lnl] EncodeLoop started");

        while (_running && !_isDisposed)
        {
            _encodeThreadEvent.WaitOne();

            if (_isDisposed) break;

            while (_messagesToTransmit.TryDequeue(out var message))
            {
                if (message.Targets.Count == 0)
                {
                    message.Dispose();
                    continue;
                }

                try
                {
                    // Update statistics
                    switch (message)
                    {
                        case DeltaBatch: TotalSentDeltas++; break;
                        case FullBatch: TotalSentFulls++; break;
                        case StreamMessage: TotalSentStreams++; break;
                        case RawFrameMessage: TotalSentRawFrames++; break;
                    }

                    // Encode and transmit. Compress the encoded frame when the message opts
                    // in (Delta/Full/Stream) and it's actually big enough to be worth it - the
                    // codec returns the frame unchanged if it wouldn't shrink, and the receiver
                    // unwraps transparently. - xlinka
                    var encoded = message.Encode();
                    int threshold = message.CompressionThreshold;
                    if (threshold > 0 && encoded.Length >= threshold)
                        encoded = SyncFrameCodec.WrapCompressed(encoded);
                    Session.Connections.Broadcast(encoded, message.Targets, message.Reliable);
                }
                catch (Exception ex)
                {
                    LumoraLogger.Error($"[lnl] EncodeLoop: Exception encoding message: {ex.Message}");
                }
                finally
                {
                    message.Dispose();
                }
            }
        }

        LumoraLogger.Log("[lnl] EncodeLoop stopped");
    }

    // SYNC THREAD

    private void SyncLoop()
    {
        LumoraLogger.Log("[lnl] SyncLoop started");

        var controlMessagesToProcess = new List<ControlMessage>();
        ulong lastDeltaSyncTime = 0;

        _syncThreadInitEvent.Set();

        while (_running && !_isDisposed)
        {
            bool lockHeld = false;
            try
            {
                DEBUG_SyncLoopStage = SyncLoopStage.WaitingForSyncThreadEvent;

                if (_messagesToProcess.IsEmpty)
                {
                    _syncThreadEvent.WaitOne(1000 / SyncRate);
                }

                if (_isDisposed) break;

                World.HookManager?.DataModelLock(Thread.CurrentThread);
                lockHeld = true;

                DEBUG_SyncLoopStage = SyncLoopStage.RunningMessageProcessing;

                // Peek-then-conditional-dequeue: we only remove a message from the queue once it's been
                // accepted. ProcessMessage returns false to DEFER (currently only the authority
                // delta-staleness guard does this) - we leave that message at the front and stop draining
                // this cycle, so it gets reprocessed next cycle after our StateVersion has advanced.
                // Dequeuing only on success is what makes that requeue free. -xlinka
                while (_messagesToProcess.TryPeek(out var message))
                {
                    ProcessingSyncMessage = message;

                    if (!ProcessMessage(message, lastDeltaSyncTime, controlMessagesToProcess))
                    {
                        // Deferred - leave it at the front, stop draining. (If anyone adds another
                        // `return false` to ProcessMessage, it stalls the drain here: false means defer.)
                        ProcessingSyncMessage = null!;
                        break;
                    }

                    ProcessingSyncMessage = null!;
                    TotalProcessedMessages++;

                    _messagesToProcess.TryDequeue(out var dequeued);
                    if (!ReferenceEquals(message, dequeued))
                    {
                        // Only this thread dequeues from the front; a changed front means something else
                        // is racing the queue and our peek/process pairing is unsound - fail loud. -xlinka
                        throw new InvalidOperationException("Sync message queue front was modified outside the sync loop");
                    }
                }

                DEBUG_SyncLoopStage = SyncLoopStage.ExitedMessageProcessing;

                ProcessPendingRecords();

                // Drive the join completeness gate every tick (not only when records are pending), so a
                // client awaiting initial state always advances toward Running and can never hang -
                // ProcessPendingRecords early-returns when nothing is pending. Self-guards on state.
                // -xlinka
                TryEnterRunningWhenComplete();

                if (World.IsAuthority)
                {
                    World.IncrementStateVersion();
                }

                World.HookManager?.DataModelUnlock();
                lockHeld = false;
                _lastProcessingStopped.Set();

                DEBUG_SyncLoopStage = SyncLoopStage.ProcessingStopped;

                if (!_refreshFinished.WaitOne(1000))
                {
                    if (_newUsersToInitialize.Count > 0)
                    {
                        LumoraLogger.Warn("[lnl] SyncLoop: Timeout waiting for world refresh, continuing anyway");
                    }
                }

                if (_isDisposed) break;

                _lastProcessingStopped.Reset();
                _refreshFinished.Reset();

                DEBUG_SyncLoopStage = SyncLoopStage.ContinuingAfterRefreshFinished;

                World.HookManager?.DataModelLock(Thread.CurrentThread);
                lockHeld = true;

                DEBUG_SyncLoopStage = SyncLoopStage.GeneratingDeltaBatch;

                var deltaBatch = World.SyncController.CollectDeltaMessages();
                LastGeneratedDeltaChanges = deltaBatch.DataRecordCount;

                if (deltaBatch.DataRecordCount > 0)
                {
                    if (World.IsAuthority)
                    {
                        AddAuthorityFanoutTargets(deltaBatch);
                    }
                    else
                    {
                        var hostConnection = Session.Connections.HostConnection;
                        if (hostConnection != null)
                        {
                            deltaBatch.Targets.Add(hostConnection);
                        }
                    }

                    if (deltaBatch.Targets.Count > 0)
                    {
                        if (!World.IsAuthority)
                        {
                            var confirmTime = deltaBatch.SenderSyncTick;
                            if (!_changesToConfirm.TryGetValue(confirmTime, out var ids))
                            {
                                ids = new HashSet<RefID>();
                                _changesToConfirm[confirmTime] = ids;
                            }

                            for (int i = 0; i < deltaBatch.DataRecordCount; i++)
                            {
                                ids.Add(deltaBatch.GetDataRecord(i).TargetID);
                            }
                        }

                        EnqueueForTransmission(deltaBatch);
                    }
                    else
                    {
                        deltaBatch.Dispose();
                    }
                }
                else
                {
                    deltaBatch.Dispose();
                }

                DEBUG_SyncLoopStage = SyncLoopStage.GeneratingCorrections;

                // A field that was being driven suppresses its deltas while the drive is active, so once
                // the drive is released peers are stuck on the last value the drive pushed. The authority
                // collects those just-freed fields here and re-sends their real current value as a small
                // full-state batch so everyone snaps back to the truth. -xlinka
                if (World.IsAuthority)
                {
                    var released = _releasedDrivesScratch;
                    released.Clear();
                    World.LinkManager?.GetReleasedDrives(released);

                    if (released.Count > 0)
                    {
                        var corrections = World.SyncController.EncodeFullBatch(released);

                        AddAuthorityFanoutTargets(corrections);

                        if (corrections.Targets.Count > 0)
                        {
                            TotalCorrections += released.Count;
                            EnqueueForTransmission(corrections);
                        }
                        else
                        {
                            corrections.Dispose();
                        }
                    }
                }

                DEBUG_SyncLoopStage = SyncLoopStage.EncodingStreams;

                if (World.State == World.WorldState.Running)
                {
                    var streams = _streamsScratch;
                    streams.Clear();
                    World.SyncController.GatherStreams(streams);

                    int sentCount = 0;
                    foreach (var stream in streams)
                    {
                        AddStreamTargets(stream, excludeSender: false);
                        if (stream.Targets.Count > 0)
                        {
                            EnqueueForTransmission(stream);
                            sentCount++;
                        }
                        else
                        {
                            stream.Dispose();
                        }
                    }

                    // Log stream transmission summary periodically (every 60 ticks = ~1 sec at 60 fps)
                    if (streams.Count > 0 && World.SyncTick % 60 == 0)
                    {
                        LumoraLogger.Log($"[lnl] [Stream] Gathered {streams.Count} messages, sent {sentCount} (LocalUser streams: {World.LocalUser?.StreamCount ?? 0})");
                    }
                }

                DEBUG_SyncLoopStage = SyncLoopStage.FinishingSyncCycle;

                World.LocalUser?.ClearJustAddedStreams();

                lastDeltaSyncTime = World.StateVersion;
                World.IncrementSyncTick();

                DEBUG_SyncLoopStage = SyncLoopStage.ProcessingControlMessages;

                foreach (var controlMessage in controlMessagesToProcess)
                {
                    ProcessControlMessage(controlMessage);
                    controlMessage.Dispose();
                }
                controlMessagesToProcess.Clear();

                // Keep a deferred join advancing - OnFullStateReceived may have held Running back waiting for
                // the local user, and every sync cycle is a chance to claim it. Cheap + idempotent. -xlinka
                World.PumpJoinProgress();

                DEBUG_SyncLoopStage = SyncLoopStage.InitializingNewUsers;

                if (World.IsAuthority)
                {
                    List<User> usersToInit = null!;
                    lock (_newUsersLock)
                    {
                        if (_newUsersToInitialize.Count > 0)
                        {
                            usersToInit = new List<User>(_newUsersToInitialize);
                            _newUsersToInitialize.Clear();
                        }
                    }

                    if (usersToInit != null && usersToInit.Count > 0)
                    {
                        LumoraLogger.Log($"[lnl] Stage 8: Encoding FullBatch for {usersToInit.Count} new users");
                        var fullBatch = World.SyncController.EncodeFullBatch();
                        LumoraLogger.Log($"[lnl] Stage 8: FullBatch has {fullBatch.DataRecordCount} records");

                        foreach (var user in usersToInit)
                        {
                            if (Session.Connections.TryGetConnection(user, out var connection))
                            {
                                LumoraLogger.Log($"[lnl] Stage 8: Adding target connection for user '{user.UserName.Value}'");
                                fullBatch.Targets.Add(connection);
                            }
                            else
                            {
                                LumoraLogger.Warn($"[lnl] Stage 8: No connection found for user '{user.UserName.Value}'");
                            }
                        }

                        LumoraLogger.Log($"[lnl] Stage 8: Enqueueing FullBatch with {fullBatch.Targets.Count} targets");
                        EnqueueForTransmission(fullBatch);

                        var startDeltaMessage = new ControlMessage(ControlMessage.Message.JoinStartDelta);
                        foreach (var user in usersToInit)
                        {
                            if (Session.Connections.TryGetConnection(user, out var connection))
                            {
                                startDeltaMessage.Targets.Add(connection);
                                // Full state + the "deltas may flow now" signal are on the wire for this
                                // user, so open its gate. The very next sync tick's fan-out will start
                                // including it. Order matters: full batch enqueued, then JoinStartDelta,
                                // then gate open - so live traffic only ever trails full state. -xlinka
                                user.StartTransmittingStreamData();
                            }
                        }
                        LumoraLogger.Log($"[lnl] Stage 8: Enqueueing JoinStartDelta with {startDeltaMessage.Targets.Count} targets");
                        EnqueueForTransmission(startDeltaMessage);
                    }
                }

                DEBUG_SyncLoopStage = SyncLoopStage.Finished;
            }
            catch (Exception ex)
            {
                LumoraLogger.Error($"[lnl] SyncLoop: Exception: {ex.Message}\n{ex.StackTrace}");
                if (World.HookManager?.Lock == HookManager.LockOwner.DataModel)
                {
                    World.HookManager?.DataModelUnlock();
                }
            }
            finally
            {
                if (lockHeld)
                {
                    World.HookManager?.DataModelUnlock();
                }
            }
        }

        LumoraLogger.Log("[lnl] SyncLoop stopped");
    }

    private bool ProcessMessage(SyncMessage msg, ulong lastDeltaSyncTime, List<ControlMessage> controlMessagesToProcess)
    {
        try
        {
            // Link sender user
            if (msg is not ControlMessage)
            {
                if (World.IsAuthority)
                {
                    if (!Session.Connections.TryGetUser(msg.Sender, out var user))
                    {
                        msg.Dispose();
                        return true;
                    }
                    msg.LinkSenderUser(user);
                }
                // Client links to host user
            }

            switch (msg)
            {
                case DeltaBatch deltaBatch:
                    // Only process deltas when world is running
                    if (World.State != World.WorldState.Running)
                    {
                        EnqueuePendingDelta(deltaBatch, $"world state {World.State}");
                        break;
                    }

                    // Authority defers a delta whose sender is at-or-ahead of where our state was at the
                    // last sync-cycle boundary. Returning false (NOT disposing) leaves it at the queue
                    // front; next cycle our StateVersion has advanced and lastDeltaSyncTime is re-captured,
                    // so validate/retransmit runs against strictly-newer authority state. This is the ONLY
                    // place ProcessMessage returns false - the drain treats false as "defer". -xlinka
                    if (World.IsAuthority && deltaBatch.SenderStateVersion >= lastDeltaSyncTime)
                    {
                        return false;
                    }

                    if (!World.IsAuthority && !_acceptDeltas)
                    {
                        EnqueuePendingDelta(deltaBatch, "JoinStartDelta not received yet");
                        break;
                    }

                    ApplyDeltaBatch(deltaBatch);
                    break;

                case FullBatch fullBatch:
                    // Track progress for initial FullBatch (client joining)
                    if (!World.IsAuthority && World.InitState == World.InitializationState.InitializingDataModel)
                    {
                        TrackFullBatchProgress(fullBatch);
                    }

                    ApplyDataRecords(fullBatch);

                    if (!World.IsAuthority)
                    {
                        World.SetStateVersion(fullBatch.SenderStateVersion);
                        // Transition happens on JoinStartDelta to avoid starting before deltas are allowed.
                    }

                    fullBatch.Dispose();
                    break;

                case ConfirmationMessage confirmation:
                    for (int i = 0; i < confirmation.DataRecordCount; i++)
                    {
                        World.SyncController.DecodeCorrection(i, confirmation);
                    }

                    if (_changesToConfirm.TryGetValue(confirmation.ConfirmTime, out var confirmedIds))
                    {
                        World.SyncController.ApplyConfirmations(confirmedIds, confirmation.ConfirmTime);

                        foreach (var id in confirmedIds)
                        {
                            World.ReferenceController?.DeleteFromTrash(id);
                        }

                        _changesToConfirm.Remove(confirmation.ConfirmTime);
                    }
                    else
                    {
                        LumoraLogger.Debug($"[lnl] Confirmation received for unknown tick {confirmation.ConfirmTime}");
                    }

                    if (!World.IsAuthority)
                    {
                        World.SetStateVersion(confirmation.SenderStateVersion);
                    }

                    confirmation.Dispose();
                    break;

                case StreamMessage streamMessage:
                    if (World.IsAuthority)
                    {
                        // Authority sees the original sender connection (no relay). A peer
                        // can put any UserID in the StreamMessage, so we must reject the
                        // message - and refuse to relay it - when the claimed UserID does
                        // not belong to the connection it actually arrived on. Otherwise a
                        // peer could claim another user's identity and we would relay that
                        // stream data to everyone else in the session.
                        if (!ValidateUserSender(streamMessage.Sender, streamMessage.UserID, "StreamMessage"))
                        {
                            streamMessage.Dispose();
                            break;
                        }

                        var forwarded = CloneStreamMessage(streamMessage);
                        AddStreamTargets(forwarded, excludeSender: true);
                        if (forwarded.Targets.Count > 0)
                        {
                            EnqueueForTransmission(forwarded);
                        }
                        else
                        {
                            forwarded.Dispose();
                        }
                    }
                    if (World.State != World.WorldState.Running)
                    {
                        // Streams are latest-value avatar poses. While we're still loading the world
                        // there's nothing to apply them to yet, and the very next stream after we go
                        // Running carries the current pose - so a queued backlog is useless and just
                        // floods the log when the load takes a moment (a joiner shouldn't be sent
                        // streams until it's ready). Drop them quietly until we're live.
                        // -xlinka
                        streamMessage.Dispose();
                        break;
                    }
                    ApplyStreamMessage(streamMessage);
                    break;

                case RawFrameMessage rawFrame:
                    if (World.IsAuthority)
                    {
                        // Same sender-identity check as StreamMessage: a peer cannot claim
                        // another user's UserID and have us relay it to everyone.
                        if (!ValidateUserSender(rawFrame.Sender, rawFrame.UserID, "RawFrameMessage"))
                        {
                            rawFrame.Dispose();
                            break;
                        }

                        var forwarded = rawFrame.CloneForRelay();
                        AddStreamTargets(forwarded, excludeSender: true);
                        if (forwarded.Targets.Count > 0)
                        {
                            EnqueueForTransmission(forwarded);
                        }
                        else
                        {
                            forwarded.Dispose();
                        }
                    }

                    DispatchRawFrame(rawFrame);
                    rawFrame.Dispose();
                    break;

                case ControlMessage controlMessage:
                    // Queue for later processing
                    controlMessagesToProcess.Add(controlMessage);
                    break;

                default:
                    LumoraLogger.Warn($"[lnl] Unknown message type: {msg.GetType()}");
                    msg.Dispose();
                    break;
            }

            return true;
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"[lnl] ProcessMessage: Exception: {ex.Message}");
            throw;
        }
    }

    private void EnqueuePendingDelta(DeltaBatch batch, string reason)
    {
        // Deltas are reliable + ordered: dropping one permanently diverges the joiner's state. While
        // we're still joining (host starts targeting us with deltas the moment it has sent our snapshot,
        // a few ticks before we reach Running) the backlog is bounded by how long the join takes and is
        // replayed in order once we're live - so we must NOT drop at the small live cap here. The live
        // cap is real backpressure and only applies once we're Running. -xlinka
        bool joining = !World.IsAuthority && World.State != World.WorldState.Running;
        int cap = joining ? MaxJoinPendingDeltaBatches : MaxPendingDeltaBatches;
        if (_pendingDeltaBatches.Count >= cap)
        {
            LumoraLogger.Warn($"[lnl] ProcessMessage: Dropping delta batch - pending limit reached ({cap})");
            batch.Dispose();
            return;
        }

        LumoraLogger.Debug($"[lnl] ProcessMessage: Queueing delta batch ({reason})");
        _pendingDeltaBatches.Enqueue(batch);
    }

    private void EnqueuePendingStream(StreamMessage streamMessage, string reason)
    {
        if (_pendingStreamMessages.Count >= MaxPendingStreamMessages)
        {
            LumoraLogger.Warn($"[lnl] ProcessMessage: Dropping stream message - pending limit reached ({MaxPendingStreamMessages})");
            streamMessage.Dispose();
            return;
        }

        LumoraLogger.Debug($"[lnl] ProcessMessage: Queueing stream message ({reason})");
        _pendingStreamMessages.Enqueue(streamMessage);
    }

    private void FlushPendingMessages()
    {
        if (World.State != World.WorldState.Running)
            return;

        if (World.IsAuthority || _acceptDeltas)
        {
            while (_pendingDeltaBatches.Count > 0)
            {
                ApplyDeltaBatch(_pendingDeltaBatches.Dequeue());
            }
        }

        while (_pendingStreamMessages.Count > 0)
        {
            ApplyStreamMessage(_pendingStreamMessages.Dequeue());
        }
    }

    private void ApplyDeltaBatch(DeltaBatch deltaBatch)
    {
        // Always validate before applying deltas; host will retransmit if needed.
        ValidateDeltaBatchAndRetransmit(deltaBatch);

        ApplyDataRecords(deltaBatch);

        if (!World.IsAuthority)
        {
            World.SetStateVersion(deltaBatch.SenderStateVersion);
        }

        deltaBatch.Dispose();
    }

    private void ApplyStreamMessage(StreamMessage streamMessage)
    {
        if (World.State == World.WorldState.Running)
        {
            World.SyncController.ApplyStreams(streamMessage);
        }
        streamMessage.Dispose();
    }

    private void ValidateDeltaBatchAndRetransmit(DeltaBatch batch)
    {
        World.SyncController.ValidateDeltaMessages(batch);

        var conflicting = new List<RefID>();
        batch.GetConflictingDataRecords(conflicting);
        batch.RemoveInvalidRecords();

        if (World.IsAuthority)
        {
            if (batch.DataRecordCount > 0)
            {
                var forward = CopyDeltaBatch(batch);

                // Relay this peer's delta to everyone else who's live, but never echo it back to the
                // sender that produced it. -xlinka
                AddAuthorityFanoutTargets(forward, batch.Sender);

                if (forward.Targets.Count > 0)
                {
                    EnqueueForTransmission(forward);
                }
                else
                {
                    forward.Dispose();
                }
            }

            if (batch.Sender != null)
            {
                // Always confirm an accepted delta back to the client that sent it - even with ZERO
                // conflicts. The client keys its own outgoing changes by this tick and only advances
                // their confirmed state (and clears its pending-confirm set) when it hears back. An empty
                // confirmation is exactly how a clean, non-conflicting change gets acknowledged; without
                // it the client's _changesToConfirm for clean ticks leaks forever and those changes never
                // register as confirmed. When there ARE conflicts we also carry the authoritative current
                // values so the client corrects. -xlinka
                var confirmation = new ConfirmationMessage(batch.SenderSyncTick, World.StateVersion, World.SyncTick);
                if (conflicting.Count > 0)
                {
                    TotalCorrections += conflicting.Count;
                    foreach (var id in conflicting)
                    {
                        World.SyncController.EncodeFull(id, confirmation);
                    }
                }
                confirmation.Targets.Add(batch.Sender);
                EnqueueForTransmission(confirmation);
            }
        }
    }

    /// <summary>
    /// Build and enqueue a <see cref="RawFrameMessage"/> for a stream the local
    /// user owns. Routes to the host on clients, fans out to all peers (including
    /// loopback to local handlers) on the authority. Returns false if the payload
    /// is over the cap or the stream isn't owned by the local user.
    /// </summary>
    public bool EnqueueRawFrame(Stream stream, ushort sequence, ReadOnlySpan<byte> payload)
    {
        if (stream == null) return false;
        if (payload.Length > NetworkLimits.MaxRawFrameBytes) return false;
        if (World == null) return false;

        var owner = stream.Owner;
        if (owner == null || !owner.IsLocal) return false;

        var msg = new RawFrameMessage(World.StateVersion, World.SyncTick)
        {
            UserID = (ulong)owner.ReferenceID,
            StreamRefID = stream.ReferenceID,
            Sequence = sequence,
        };
        msg.SetPayload(payload);

        AddStreamTargets(msg, excludeSender: false);
        if (msg.Targets.Count == 0)
        {
            msg.Dispose();
            return false;
        }

        EnqueueForTransmission(msg);
        return true;
    }

    /// <summary>
    /// Resolve the claimed user from a received <see cref="RawFrameMessage"/> and
    /// invoke the Session-level handler. Runs on the sync thread; subscribers
    /// must not block (push to a lock-free queue and return).
    /// </summary>
    private void DispatchRawFrame(RawFrameMessage rawFrame)
    {
        if (rawFrame == null || World == null) return;

        var userElement = World.ReferenceController?.GetObjectOrNull(new RefID(rawFrame.UserID));
        if (userElement is not User sender)
        {
            LumoraLogger.Warn($"[lnl] RawFrame: user {rawFrame.UserID} not found; dropping.");
            return;
        }

        Session.HandleIncomingRawFrame(sender, rawFrame.StreamRefID, rawFrame.Sequence, rawFrame.Payload);
    }

    /// <summary>
    /// Verify a user-attributed message's claimed UserID matches the user mapped
    /// to the sender connection. Authority-side use only - clients receive
    /// messages relayed via the host, whose Sender is the host connection rather
    /// than the original peer.
    /// </summary>
    private bool ValidateUserSender(IConnection sender, ulong claimedUserID, string typeName)
    {
        if (sender == null)
        {
            LumoraLogger.Warn($"[lnl] {typeName} with no Sender; dropping.");
            return false;
        }

        if (!Session.Connections.TryGetUser(sender, out var senderUser) || senderUser == null)
        {
            LumoraLogger.Warn($"[lnl] {typeName} from {sender.Identifier}: no user mapping; dropping.");
            return false;
        }

        if ((ulong)senderUser.ReferenceID != claimedUserID)
        {
            LumoraLogger.Warn($"[lnl] {typeName} sender mismatch: connection {sender.Identifier} is user {senderUser.UserName?.Value} ({(ulong)senderUser.ReferenceID}) but claimed UserID {claimedUserID}; dropping.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Populate <paramref name="message"/>.Targets with the appropriate fan-out for
    /// stream-class messages: on the authority, every other connection (optionally
    /// excluding the sender of a relayed message and any users still initializing);
    /// on a client, just the host. Used for both <see cref="StreamMessage"/> and
    /// <see cref="RawFrameMessage"/>.
    /// </summary>
    /// <summary>
    /// Adds every connection that should receive an authority-side replicated batch (delta, correction,
    /// or stream) as a target on <paramref name="message"/>. A user becomes eligible only once it has
    /// been handed full world state - its <see cref="User.ReceiveStreams"/> flips true at the JoinStartDelta
    /// point - so until then we leave it out and let its initial full batch carry the state instead;
    /// sending it live traffic earlier would just race the join. A connection with no mapped user yet
    /// (mid-handshake) is always included so nothing is silently dropped. Pass the original sender as
    /// <paramref name="excludeConnection"/> to avoid echoing a relayed message back where it came from.
    ///
    /// This is the single fan-out walk shared by the delta batch, released-drive corrections, the stream
    /// loop, and relayed-delta retransmit. It replaced four separate per-tick `new HashSet<User>()`
    /// snapshots of the init queue - the ReceiveStreams flag already carries that "still initializing"
    /// state, so the per-message allocation and lock round-trip were pure waste. -xlinka
    /// </summary>
    private void AddAuthorityFanoutTargets(SyncMessage message, IConnection? excludeConnection = null)
    {
        var connections = Session.Connections.GetAllConnections();
        foreach (var connection in connections)
        {
            if (excludeConnection != null && connection == excludeConnection)
            {
                continue;
            }

            // A mapped user that hasn't been handed full state yet (or opted out) is skipped. An unmapped
            // connection still gets included to match the prior behavior. -xlinka
            if (Session.Connections.TryGetUser(connection, out var user) && user != null && !user.ReceiveStreams)
            {
                continue;
            }

            message.Targets.Add(connection);
        }
    }

    private void AddStreamTargets(SyncMessage message, bool excludeSender)
    {
        if (World.IsAuthority)
        {
            AddAuthorityFanoutTargets(message, excludeSender ? message.Sender : null);
        }
        else
        {
            var hostConnection = Session.Connections.HostConnection;
            if (hostConnection != null)
            {
                message.Targets.Add(hostConnection);
            }
        }
    }

    private static StreamMessage CloneStreamMessage(StreamMessage source)
    {
        var clone = new StreamMessage(source.SenderStateVersion, source.SenderSyncTick)
        {
            IsAsynchronous = source.IsAsynchronous,
            UserID = source.UserID,
            StreamStateVersion = source.StreamStateVersion,
            StreamTime = source.StreamTime,
            StreamGroup = source.StreamGroup
        };

        var sourceData = source.GetData();
        sourceData.Position = 0;
        var cloneData = clone.GetData();
        sourceData.CopyTo(cloneData);

        return clone;
    }

    private DeltaBatch CopyDeltaBatch(DeltaBatch source)
    {
        var copy = new DeltaBatch(World.StateVersion, World.SyncTick);

		for (int i = 0; i < source.DataRecordCount; i++)
		{
			var record = source.GetDataRecord(i);
			var length = record.EndOffset - record.StartOffset;
			var reader = source.SeekDataRecord(i);
			var buffer = length > 0 ? reader.ReadBytes(length) : Array.Empty<byte>();

			var writer = copy.BeginNewDataRecord(record.TargetID);
			if (buffer.Length > 0)
			{
				writer.Write(buffer);
			}
			copy.FinishDataRecord(record.TargetID);
		}

        return copy;
    }

    private int ApplyDataRecords(BinaryMessageBatch batch)
    {
        // Simple retry logic, no complex dependency resolution
        int remaining = batch.DataRecordCount;
        int passes = 0;

        // LumoraLogger.Log($"[lnl] ApplyDataRecords: Starting with {batch.DataRecordCount} records, isFull={batch is FullBatch}");

        while (remaining > 0)
        {
            int startRemaining = remaining;
            int decodedThisPass = 0;

            for (int i = 0; i < batch.DataRecordCount; i++)
            {
                if (batch.IsProcessed(i))
                    continue;

                bool decoded = false;
                try
                {
                    decoded = batch switch
                    {
                        DeltaBatch delta => World.SyncController.DecodeDeltaMessage(i, delta),
                        FullBatch full => World.SyncController.DecodeFullMessage(i, full),
                        _ => false
                    };

                    if (decoded)
                    {
                        batch.MarkDataRecordAsProcessed(i);
                        remaining--;
                        decodedThisPass++;
                        // Count records that actually MATERIALIZED, so the loading bar reflects real
                        // progress (this counter used to be declared but never incremented). -xlinka
                        if (batch is FullBatch && !World.IsAuthority)
                        {
                            lock (_progressLock) { _initializedComponents++; }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log but don't retry immediately - might succeed in next pass
                    var dataRecord = batch.GetDataRecord(i);
                    LumoraLogger.Debug($"[lnl] ApplyDataRecords: Decode failed for {dataRecord.TargetID}: {ex.Message}");
                }
            }

            // LumoraLogger.Log($"[lnl] ApplyDataRecords: Pass {passes} decoded {decodedThisPass} records, {remaining} remaining");

            // If no progress in this pass, we're done
            if (remaining == startRemaining)
            {
                break;
            }

            passes++;
        }

        // Log any remaining failures with details
        if (remaining > 0)
        {
            LumoraLogger.Debug($"[lnl] ApplyDataRecords: {remaining} records could not be decoded after {passes} passes");
            // Log which RefIDs failed
            for (int i = 0; i < batch.DataRecordCount; i++)
            {
                if (!batch.IsProcessed(i))
                {
                    var record = batch.GetDataRecord(i);
                    var obj = World.ReferenceController?.GetObjectOrNull(record.TargetID);
                    LumoraLogger.Debug($"[lnl]   FAILED RefID={record.TargetID} Type={obj?.GetType().Name ?? "NOT_FOUND"}");
                    QueuePendingRecord(batch, i);
                }
            }
        }

        return remaining;
    }

    private void QueuePendingRecord(BinaryMessageBatch batch, int recordIndex)
    {
        if (World == null)
            return;

        var record = batch.GetDataRecord(recordIndex);
        var reader = batch.SeekDataRecord(recordIndex);
        var length = record.EndOffset - record.StartOffset;
        var buffer = length > 0 ? reader.ReadBytes(length) : Array.Empty<byte>();

        var pending = new PendingRecord
        {
            TargetID = record.TargetID,
            Data = buffer,
            SenderStateVersion = batch.SenderStateVersion,
            SenderSyncTick = batch.SenderSyncTick,
            FirstSeenTick = World.SyncTick,
            Attempts = 0
        };

        var isFull = batch is FullBatch;
        var map = isFull ? _pendingFullRecords : _pendingDeltaRecords;

        lock (_pendingLock)
        {
            if (map.TryGetValue(record.TargetID, out var existing))
            {
                if (pending.SenderSyncTick >= existing.SenderSyncTick)
                {
                    map[record.TargetID] = pending;
                }
            }
            else
            {
                map[record.TargetID] = pending;
            }
        }
    }

    private void ProcessPendingRecords()
    {
        if (World == null || World.ReferenceController == null || World.SyncController == null)
            return;

        List<PendingRecord> fullRecords;
        List<PendingRecord> deltaRecords;

        lock (_pendingLock)
        {
            if (_pendingFullRecords.Count == 0 && _pendingDeltaRecords.Count == 0)
                return;

            fullRecords = new List<PendingRecord>(_pendingFullRecords.Values);
            deltaRecords = new List<PendingRecord>(_pendingDeltaRecords.Values);
        }

        foreach (var record in fullRecords)
        {
            ProcessPendingRecord(record, isFull: true);
        }

        // Initial-state records just got a retry pass; see if the snapshot is now complete enough to
        // enter the world (or stuck long enough that we should enter anyway and say what's missing).
        TryEnterRunningWhenComplete();

        if (World.State == World.WorldState.Running && (_acceptDeltas || World.IsAuthority))
        {
            foreach (var record in deltaRecords)
            {
                ProcessPendingRecord(record, isFull: false);
            }
        }
    }

    // Flip the joined world to Running only after every record of the initial full state has been
    // applied: hold until the pending-full-record set drains to empty, then transition. If progress stalls for a long stretch (an orphaned record whose owner
    // never decoded - e.g. a component whose type couldn't resolve), we log exactly what's stuck and
    // enter anyway, so a join surfaces the problem loudly instead of either bricking or silently
    // showing a half-world. -xlinka
    private void TryEnterRunningWhenComplete()
    {
        if (World == null || World.IsAuthority) return;
        if (!_joinStartDeltaSeen) return;
        if (World.InitState != World.InitializationState.InitializingDataModel) return;

        int pending;
        lock (_pendingLock)
        {
            pending = _pendingFullRecords.Count;
        }

        // Clean case: the batch was tracked and everything applied - enter right away.
        if (pending == 0 && _initialFullBatchReceived)
        {
            LumoraLogger.Log("[lnl] Initial full state fully applied - entering world (Running)");
            World.OnFullStateReceived();
            return;
        }

        // Otherwise fall through to the stall detector below, which always force-enters after a short
        // window. Crucially we do NOT hard-require _initialFullBatchReceived here: if that flag never
        // got set the join must still become usable rather than hang forever. -xlinka

        // Still applying. Track progress so we can tell "slowly resolving" from "genuinely stuck".
        if (pending != _lastInitialPendingCount)
        {
            _lastInitialPendingCount = pending;
            _lastInitialProgressTick = World.SyncTick;
            return;
        }

        if (World.SyncTick - _lastInitialProgressTick < (ulong)InitialStateStuckTicks)
            return;

        // Stuck. Before giving up, ask the host to resend the full
        // state - missing records often just need another delivery. A deterministic failure (a type
        // that won't resolve) won't recover, so we cap the retries and then enter with diagnostics
        // rather than loop forever. -xlinka
        if (_fullStateRequestBudget > 0)
        {
            _fullStateRequestBudget--;
            LumoraLogger.Warn($"[lnl] Initial state stuck with {pending} record(s) - re-requesting full state from host ({_fullStateRequestBudget} retries left)");
            var request = new ControlMessage(ControlMessage.Message.RequestFullState);
            var host = Session.Connections.HostConnection;
            if (host != null)
            {
                request.Targets.Add(host);
                EnqueueForTransmission(request);
            }
            // Reset the stall detector so we wait for the resent batch instead of re-firing every tick.
            _lastInitialProgressTick = World.SyncTick;
            _lastInitialPendingCount = -1;
            return;
        }

        if (!_loggedInitialIncomplete)
        {
            _loggedInitialIncomplete = true;
            LumoraLogger.Error($"[lnl] Initial full state STUCK with {pending} record(s) that never resolved their owner - entering world INCOMPLETE. Unresolved:");
            lock (_pendingLock)
            {
                foreach (var kvp in _pendingFullRecords)
                {
                    var obj = World.ReferenceController?.GetObjectOrNull(kvp.Key);
                    LumoraLogger.Error($"[lnl]   unresolved RefID={kvp.Key} owner={(obj?.GetType().Name ?? "NEVER CREATED")}");
                }
            }
        }

        LumoraLogger.Warn("[lnl] Entering world despite incomplete initial state (see unresolved records above)");
        World.OnFullStateReceived();
    }

    private void ProcessPendingRecord(PendingRecord record, bool isFull)
    {
        if (World == null || World.ReferenceController == null || World.SyncController == null)
            return;

        // During the initial join we keep retrying instead of giving
        // up after a few frames - a record is usually only pending because its owner hasn't decoded
        // yet, and dropping it is permanent lost world content. We use a much longer ceiling here and,
        // if we ever do drop, we say loudly that it cost world content. -xlinka
        bool initialState = isFull && !World.IsAuthority
            && World.InitState == World.InitializationState.InitializingDataModel;
        ulong maxAge = (ulong)(initialState ? InitialStateMaxAgeTicks : PendingRecordMaxAgeTicks);

        var isExpired = World.SyncTick - record.FirstSeenTick > maxAge;
        if (isExpired)
        {
            if (initialState)
            {
                var owner = World.ReferenceController.GetObjectOrNull(record.TargetID);
                DropPendingRecord(record.TargetID, isFull,
                    $"expired during initial state - MISSING WORLD CONTENT (owner={owner?.GetType().Name ?? "never created"})");
            }
            else
            {
                DropPendingRecord(record.TargetID, isFull, "expired");
            }
            return;
        }

        if (!World.ReferenceController.ContainsObject(record.TargetID))
        {
            return;
        }

        if (!isFull)
        {
            var syncElement = World.ReferenceController.GetObjectOrNull(record.TargetID) as SyncElement;
            if (syncElement != null && syncElement.IsSyncDirty)
            {
                // Defer pending delta while local changes are still dirty.
                return;
            }
        }

        var decoded = TryDecodePendingRecord(record, isFull);
        if (decoded)
        {
            if (isFull && !World.IsAuthority)
            {
                lock (_progressLock) { _initializedComponents++; }
            }
            DropPendingRecord(record.TargetID, isFull, null!);
            return;
        }

        record.Attempts++;
        // No per-attempt drop during initial state - the age ceiling above is the only give-up there,
        // so a record whose owner is still on its way isn't discarded early. -xlinka
        int maxAttempts = initialState ? int.MaxValue : PendingRecordMaxAttempts;
        if (record.Attempts >= maxAttempts)
        {
            DropPendingRecord(record.TargetID, isFull, "attempts exceeded");
            return;
        }

        UpdatePendingRecord(record, isFull);
    }

    private bool TryDecodePendingRecord(PendingRecord record, bool isFull)
    {
        BinaryMessageBatch batch = isFull
            ? new FullBatch(record.SenderStateVersion, record.SenderSyncTick)
            : new DeltaBatch(record.SenderStateVersion, record.SenderSyncTick);

        try
        {
            var writer = batch.BeginNewDataRecord(record.TargetID);
            if (record.Data.Length > 0)
            {
                writer.Write(record.Data);
            }
            batch.FinishDataRecord(record.TargetID);

            return isFull
                ? World.SyncController.DecodeFullMessage(0, (FullBatch)batch)
                : World.SyncController.DecodeDeltaMessage(0, (DeltaBatch)batch);
        }
        catch (InvalidOperationException ex)
        {
            // Common case: delta applied to a locally dirty element. Keep pending for later.
            LumoraLogger.Debug($"[lnl] TryDecodePendingRecord: Deferred {record.TargetID} ({(isFull ? "full" : "delta")}) - {ex.Message}");
            return false;
        }
        finally
        {
            batch.Dispose();
        }
    }

    private void UpdatePendingRecord(PendingRecord record, bool isFull)
    {
        lock (_pendingLock)
        {
            var map = isFull ? _pendingFullRecords : _pendingDeltaRecords;
            if (map.ContainsKey(record.TargetID))
            {
                map[record.TargetID] = record;
            }
        }
    }

    private void DropPendingRecord(RefID targetId, bool isFull, string reason)
    {
        lock (_pendingLock)
        {
            var map = isFull ? _pendingFullRecords : _pendingDeltaRecords;
            if (map.Remove(targetId) && !string.IsNullOrEmpty(reason))
            {
                // Per-record, at Debug: a joiner can pend+drop dozens of records (e.g. its own avatar
                // echoed back before its local copy exists), and a Warn-per-record buries the console.
                // The real avatar-on-join sync gap is tracked separately. -xlinka
                LumoraLogger.Debug($"[lnl] Pending record dropped for {targetId}: {reason}");
            }
        }
    }

    private struct PendingRecord
    {
        public RefID TargetID;
        public byte[] Data;
        public ulong SenderStateVersion;
        public ulong SenderSyncTick;
        public ulong FirstSeenTick;
        public int Attempts;
    }

    /// <summary>
    /// Track progress of initial FullBatch reception for client joining.
    /// </summary>
    private void TrackFullBatchProgress(FullBatch fullBatch)
    {
        lock (_progressLock)
        {
            if (!_initialFullBatchReceived)
            {
                _initialFullBatchReceived = true;
                _expectedComponents = fullBatch.DataRecordCount;
                LumoraLogger.Log($"[lnl] TrackFullBatchProgress: Expecting {_expectedComponents} components from initial FullBatch");
            }

            // Count successfully received components
            for (int i = 0; i < fullBatch.DataRecordCount; i++)
            {
                var record = fullBatch.GetDataRecord(i);
                if (!_receivedComponentIds.Contains(record.TargetID))
                {
                    _receivedComponentIds.Add(record.TargetID);
                    _receivedComponents++;
                }
            }

            LumoraLogger.Log($"[lnl] TrackFullBatchProgress: Received {_receivedComponents}/{_expectedComponents} components ({(_receivedComponents * 100.0f / _expectedComponents):F1}%)");
        }
    }

    // NOTE: World transitions to Running when JoinStartDelta is received,
    // not based on percentage of components synchronized.

    /// <summary>
    /// Get current initialization progress (0.0 to 1.0) for loading indicators.
    /// </summary>
    public float GetInitializationProgress()
    {
        lock (_progressLock)
        {
            if (!_initialFullBatchReceived || _expectedComponents == 0)
                return 0f;

            return MathF.Min(1f, (float)_initializedComponents / _expectedComponents);
        }
    }

    /// <summary>
    /// Get current initialization status text for loading indicators.
    /// </summary>
    public string GetInitializationStatus()
    {
        if (World.IsAuthority)
            return "Authority";

        if (World.InitState == World.InitializationState.Failed)
            return "Connection failed";

        if (World.InitState == World.InitializationState.WaitingForJoinGrant)
            return "Waiting for join approval...";

        if (World.InitState == World.InitializationState.InitializingNetwork)
            return "Connecting to session...";

        lock (_progressLock)
        {
            if (!_initialFullBatchReceived)
                return "Waiting for world data...";

            if (_expectedComponents == 0)
                return "Initializing...";

            var progress = (int)((float)_initializedComponents / _expectedComponents * 100f);
            return $"Loading world components... {_initializedComponents}/{_expectedComponents} ({progress}%)";
        }
    }

    private void ProcessControlMessage(ControlMessage message)
    {
        switch (message.ControlMessageType)
        {
            case ControlMessage.Message.JoinRequest:
                if (World.IsAuthority)
                {
                    if (message.Payload == null || message.Payload.Length == 0)
                    {
                        LumoraLogger.Warn("[lnl] ProcessControlMessage: JoinRequest missing payload");
                        return;
                    }

                    var requestData = LegacyJoinRequestData.Decode(message.Payload);
                    LumoraLogger.Log($"[lnl] ProcessControlMessage: JoinRequest from '{requestData.UserName}'");
                    Session.Connections.HandleJoinRequest(message.Sender, requestData);
                }
                break;

            case ControlMessage.Message.JoinChallenge:
                // Client side only: the host wants us to sign a nonce to prove our machine identity.
                if (!World.IsAuthority)
                {
                    if (message.Payload == null || message.Payload.Length == 0)
                    {
                        LumoraLogger.Warn("[lnl] ProcessControlMessage: JoinChallenge missing payload");
                        return;
                    }
                    var challengeData = LegacyJoinChallengeData.Decode(message.Payload);
                    Session.Connections.HandleJoinChallenge(message.Sender, challengeData);
                }
                break;

            case ControlMessage.Message.JoinAuthenticate:
                // Host side only: the joiner answered our challenge; verify it before granting.
                if (World.IsAuthority)
                {
                    if (message.Payload == null || message.Payload.Length == 0)
                    {
                        LumoraLogger.Warn("[lnl] ProcessControlMessage: JoinAuthenticate missing payload");
                        return;
                    }
                    var authData = LegacyJoinAuthenticateData.Decode(message.Payload);
                    // Fire-and-forget: account verification may hit the backend (async), and the handler
                    // grants or rejects internally, so we don't block the control-message loop on it. -xlinka
                    _ = Session.Connections.HandleJoinAuthenticate(message.Sender, authData);
                }
                break;

            case ControlMessage.Message.JoinGrant:
                if (message.Payload == null || message.Payload.Length == 0)
                {
                    LumoraLogger.Warn("[lnl] ProcessControlMessage: JoinGrant missing payload");
                    return;
                }

                var grantData = LegacyJoinGrantData.Decode(message.Payload);
                LumoraLogger.Log($"[lnl] ProcessControlMessage: JoinGrant UserID={grantData.AssignedUserID}");

                var assignedRefID = new RefID(grantData.AssignedUserID);

                // Store the assigned RefID and wait for the User to sync from host.
                // When User syncs and its RefID matches, it will call SetLocalUser(this).
                LocalUserRefIDToInit = assignedRefID;
                _pendingLocalUserName = Environment.MachineName;
                _pendingAllocationStart = grantData.AllocationIDStart;
                _pendingAllocationEnd = grantData.AllocationIDEnd;

                LumoraLogger.Log($"[lnl] ProcessControlMessage: Stored pending local user RefID {assignedRefID} - waiting for User to sync from host");

                Session.World.SetStateVersion(grantData.StateVersion);

                // Scope new client-side allocations (the local user's tracking streams + the rigs that
                // InteractionLaser/HandTool/ControllerHandVisual/LocomotionController build at OnStart) into the
                // joining user's OWN byte. Without this they allocate in authority byte 0 and collide with the
                // host's objects there -> "Exception during initializing Worker of type Slot" and the laser/hands/
                // locomotion never build. AllocationIDStart is the high start-of-range value the host reserved for
                // us, so using it as the start position keeps us safely above the User object + its own synced
                // members (which sit at low positions in this byte). Seed the owned-block high-water to match. -xlinka
                var userByte = assignedRefID.GetUserByte();
                var startPos = grantData.AllocationIDStart > 0 ? grantData.AllocationIDStart : 1UL;
                World.ReferenceController.SetAllocationContext(userByte, startPos);
                World.ReferenceController.SetOwnedStartPosition(userByte, startPos);
                LumoraLogger.Log($"[lnl] Scoped allocation to user namespace: byte={userByte}, startPos={startPos}");

                Session.World.OnJoinGrantReceived();
                break;

            case ControlMessage.Message.JoinReject:
                var rejectData = message.Payload != null && message.Payload.Length > 0
                    ? LegacyJoinRejectData.Decode(message.Payload)
                    : new LegacyJoinRejectData { Reason = "Join rejected" };
                var reason = string.IsNullOrWhiteSpace(rejectData.Reason) ? "Join rejected" : rejectData.Reason;
                LumoraLogger.Warn($"[lnl] ProcessControlMessage: Join rejected - {reason}");

                if (!World.IsAuthority)
                {
                    World.InitializationFailed(reason);
                    Session.Connections.HostConnection?.Close();
                }
                break;

            case ControlMessage.Message.JoinStartDelta:
                LumoraLogger.Log("[lnl] ProcessControlMessage: JoinStartDelta received - can now accept delta updates");
                _acceptDeltas = true;
                _joinStartDeltaSeen = true;
                if (!World.IsAuthority && World.InitState == World.InitializationState.InitializingDataModel)
                {
                    // Enter the world now. The host considers us initialized once it has sent the full
                    // batch, so it starts pushing deltas immediately - if we DON'T go Running here those
                    // reliable deltas pile up at the pending cap and get dropped (= permanently lost
                    // state). Records that didn't decode yet keep retrying in the background; if any
                    // never resolve the WorkerManager/decoder logs say exactly which type/element. We
                    // surface that here instead of blocking the whole join on it. -xlinka
                    int stillPending;
                    lock (_pendingLock) { stillPending = _pendingFullRecords.Count; }
                    if (stillPending > 0)
                    {
                        LumoraLogger.Warn($"[lnl] Entering world with {stillPending} initial record(s) still pending - they'll keep retrying. If content is missing, look above for 'UNRESOLVED TYPE' / 'Unknown component type'.");
                    }
                    LumoraLogger.Log("[lnl] JoinStartDelta: transitioning client world to Running");
                    World.OnFullStateReceived();
                }
                FlushPendingMessages();
                break;

            case ControlMessage.Message.RequestFullState:
                LumoraLogger.Log("[lnl] ProcessControlMessage: RequestFullState received from client");
                
                if (World.IsAuthority && Session.Connections.TryGetUser(message.Sender, out var requestingUser))
                {
                    LumoraLogger.Log($"[lnl] ProcessControlMessage: Sending full world state to user {requestingUser.UserName.Value}");
                    var fullBatch = World.SyncController.EncodeFullBatch();
                    fullBatch.Targets.Add(message.Sender);
                    EnqueueForTransmission(fullBatch);

                    // Send JoinStartDelta to indicate they can now receive delta updates
                    var startDeltaMessage = new ControlMessage(ControlMessage.Message.JoinStartDelta);
                    startDeltaMessage.Targets.Add(message.Sender);
                    EnqueueForTransmission(startDeltaMessage);

                    // This join path does NOT go through the Stage-8 init queue, so it's the only place
                    // this user's fan-out gate gets opened. Miss it and the user receives zero deltas or
                    // streams forever - full state arrived but then nothing ever moves. Full batch is
                    // enqueued before this, so live traffic still trails the state. -xlinka
                    requestingUser.StartTransmittingStreamData();
                }
                else if (!World.IsAuthority)
                {
                    LumoraLogger.Warn("[lnl] ProcessControlMessage: Non-authority received RequestFullState - ignoring");
                }
                break;

            case ControlMessage.Message.AssetRequest:
            case ControlMessage.Message.AssetTransmissionStart:
            case ControlMessage.Message.AssetChunk:
            case ControlMessage.Message.AssetNextChunkRequest:
            case ControlMessage.Message.AssetNotAvailable:
                Session.AssetTransferer?.ProcessMessage(message);
                break;

            default:
                LumoraLogger.Log($"[lnl] ProcessControlMessage: {message.ControlMessageType}");
                break;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _running = false;

        // Signal all threads to wake up and exit
        _decodeThreadEvent.Set();
        _encodeThreadEvent.Set();
        _syncThreadEvent.Set();
        _refreshFinished.Set();

        // Wait for threads to finish
        _decodeThread?.Join(1000);
        _encodeThread?.Join(1000);
        _syncThread?.Join(1000);

        // Dispose events
        _decodeThreadEvent.Dispose();
        _encodeThreadEvent.Dispose();
        _syncThreadEvent.Dispose();
        _syncThreadInitEvent.Dispose();
        _refreshFinished.Dispose();
        _lastProcessingStopped.Dispose();
        _changesToConfirm.Clear();
        lock (_pendingLock)
        {
            _pendingFullRecords.Clear();
            _pendingDeltaRecords.Clear();
        }
        while (_pendingDeltaBatches.Count > 0)
        {
            _pendingDeltaBatches.Dequeue().Dispose();
        }
        while (_pendingStreamMessages.Count > 0)
        {
            _pendingStreamMessages.Dequeue().Dispose();
        }

        LumoraLogger.Log("[lnl] SessionSyncManager disposed");
    }
}

