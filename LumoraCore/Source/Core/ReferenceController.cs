// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core;

/// <summary>
/// Central controller for world element references.
/// Handles registration, lookup, async resolution, and allocation context management.
/// Supports allocation blocking during decode/init.
/// </summary>
public class ReferenceController : IDisposable
{
    /// <summary>
    /// If true, new RefID allocations are blocked to prevent wrong-context creation.
    /// </summary>
    public bool BlockAllocations { get; set; }

    /// <summary>
    /// Nesting counter for allocation blocking.
    /// Allocations are blocked when this is > 0.
    /// </summary>
    private int _allocationBlockCount;

    /// <summary>
    /// Whether allocations are hard-blocked (will throw).
    /// Soft blocks during OnAwake only warn.
    /// </summary>
    public bool AllocationsBlocked => BlockAllocations;

    /// <summary>
    /// Whether we're in a soft-block context (OnAwake) that will warn on allocation.
    /// </summary>
    public bool InSoftBlockContext => _allocationBlockCount > 0;

    /// <summary>
    /// Whether the world is currently decoding a batch (slots/components should defer full init).
    /// </summary>
    public bool IsDecodingBatch { get; private set; }

    /// <summary>
    /// Begin blocking allocations (can be nested).
    /// Used during OnAwake to prevent components from creating new objects.
    /// </summary>
    public void BlockAllocationsStart()
    {
        _allocationBlockCount++;
    }

    /// <summary>
    /// End blocking allocations (can be nested).
    /// </summary>
    public void BlockAllocationsEnd()
    {
        if (_allocationBlockCount > 0)
            _allocationBlockCount--;
    }

    /// <summary>
    /// Begin batch decode mode. Slots/components should defer full initialization.
    /// </summary>
    public void BeginBatchDecode()
    {
        IsDecodingBatch = true;
    }

    /// <summary>
    /// End batch decode mode.
    /// </summary>
    public void EndBatchDecode()
    {
        IsDecodingBatch = false;
    }

    // Object registry
    private readonly Dictionary<RefID, IWorldElement> _objects = new();
    
    // Pending reference requests (for objects not yet created)
    private readonly Dictionary<RefID, List<IWorldElementReceiver>> _pendingRequests = new();
    
    // Allocation context stack
    private readonly Stack<AllocationContext> _allocationStack = new();
    private AllocationContext _currentAllocation;
    
    // Local allocation tracking
    private ulong _localAllocationPosition = 1;
    private int _localAllocationDepth;

    // Per-user-byte high-water position for owned allocation blocks. Seeded from the join grant via
    // SetOwnedStartPosition, carried forward across re-entry so an owned block always mints fresh IDs. -xlinka
    private readonly ulong[] _ownedAllocationPosition = new ulong[256];
    private ulong GetOwnedPosition(byte b) => _ownedAllocationPosition[b] == 0 ? 1UL : _ownedAllocationPosition[b];

    // Highest position ever REGISTERED per byte, whoever minted it. A joiner mints its own IDs client
    // side and they only reach us as registrations, so this is the only place that sees how far a
    // user's byte actually got. The allocator reads it to start a recycled byte past the previous
    // occupant instead of replaying their positions. Deliberately not cleared by PurgeUserByte:
    // forgetting the mark is exactly what would let a rejoin collide with in-flight references to the
    // old objects. -xlinka
    private readonly ulong[] _latestUserPosition = new ulong[256];

    // Per user byte, where the host's range for that user's scripts starts, and where the next thing they
    // make is minted. Set from the join grant on every machine that knows the grant; zero is no range.
    // A user's own machine never mints at or past the fence, and the authority refuses it if it tries,
    // so what the host mints there can never meet an id the user minted. -xlinka
    private readonly ulong[] _scriptFence = new ulong[256];
    private readonly ulong[] _scriptPosition = new ulong[256];
    
    // Trash bin integration (for restore from trash)
    private readonly Dictionary<RefID, TrashEntry> _trashedObjects = new();
    
    /// <summary>
    /// The world this controller belongs to.
    /// </summary>
    public World World { get; }
    
    /// <summary>
    /// All registered objects.
    /// </summary>
    public IEnumerable<KeyValuePair<RefID, IWorldElement>> AllObjects => _objects;
    
    /// <summary>
    /// Number of registered objects.
    /// </summary>
    public int ObjectCount => _objects.Count;
    
    /// <summary>
    /// Number of pending reference requests.
    /// </summary>
    public int PendingRequestCount => _pendingRequests.Count;
    
    /// <summary>
    /// Current allocation context info (for debugging).
    /// </summary>
    public string AllocationContextInfo => 
        $"User:{_currentAllocation.UserByte} Pos:{_currentAllocation.Position} Depth:{_allocationStack.Count}";
    
    public ReferenceController(World world)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        
        // Initialize with authority allocation context
        _currentAllocation = new AllocationContext
        {
            UserByte = RefIDConstants.AUTHORITY_BYTE,
            Position = 1,
            NestedDepth = 0
        };
    }
    
    #region Object Registration
    
    /// <summary>
    /// Register a world element with the controller.
    /// Called during element initialization.
    /// </summary>
    public void RegisterObject(IWorldElement element)
    {
        if (element == null)
            throw new ArgumentNullException(nameof(element));
        
        RefID id = element.ReferenceID;
        
        if (id.IsNull)
            throw new ArgumentException("Cannot register element with null RefID", nameof(element));
        
        if (_objects.ContainsKey(id))
        {
            var existing = _objects[id];
            throw new InvalidOperationException(
                $"RefID collision! {id} already registered to {existing.GetType().Name}, " +
                $"cannot register {element.GetType().Name}");
        }
        
        _objects[id] = element;

        ulong position = id.GetPosition();
        byte userByte = id.GetUserByte();
        if (position > _latestUserPosition[userByte])
            _latestUserPosition[userByte] = position;

        // Process any pending requests for this ID
        if (_pendingRequests.TryGetValue(id, out var receivers))
        {
            _pendingRequests.Remove(id);
            foreach (var receiver in receivers)
            {
                try
                {
                    receiver.OnWorldElementAvailable(element);
                }
                catch (Exception ex)
                {
                    LumoraLogger.Error($"Exception in OnWorldElementAvailable: {ex}");
                }
            }
            // Return list to pool if using pooling
        }
    }
    
    /// <summary>
    /// Unregister a world element from the controller.
    /// Called during element destruction.
    /// </summary>
    public void UnregisterObject(IWorldElement element)
    {
        if (element == null) return;
        _objects.Remove(element.ReferenceID);
    }
    
    /// <summary>
    /// Unregister by RefID directly.
    /// </summary>
    public void UnregisterObject(in RefID id)
    {
        _objects.Remove(id);
    }

    /// <summary>
    /// Remove every registered object that lives in a user's RefID byte. Called when a user leaves or a
    /// half-finished join is torn down, so the byte can be recycled to a future joiner without that joiner
    /// colliding with the previous occupant's leftovers (streams, body slots, etc.). Without this, a leave
    /// or a failed join leaves the streams registered and the next user on this byte throws
    /// "RefID collision! User[NNN]:... already registered". -xlinka
    /// </summary>
    public int PurgeUserByte(byte userByte)
    {
        if (!RefIDConstants.IsValidUserByte(userByte))
            return 0;

        List<RefID> toRemove = null!;
        foreach (var key in _objects.Keys)
        {
            if (key.GetUserByte() == userByte)
                (toRemove ??= new List<RefID>()).Add(key);
        }

        if (toRemove == null)
            return 0;

        foreach (var key in toRemove)
            _objects.Remove(key);

        LumoraLogger.Log($"ReferenceController: purged {toRemove.Count} leftover objects for user byte {userByte}");
        return toRemove.Count;
    }
    
    #endregion
    
    #region Object Lookup
    
    /// <summary>
    /// Get an object by RefID, or null if not found.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IWorldElement GetObjectOrNull(in RefID id)
    {
        _objects.TryGetValue(id, out var element);
        return element!;
    }
    
    /// <summary>
    /// Get an object by RefID, or throw if not found.
    /// </summary>
    public IWorldElement GetObjectOrThrow(in RefID id)
    {
        if (_objects.TryGetValue(id, out var element))
            return element;
        
        throw new KeyNotFoundException($"No object found with RefID {id}");
    }
    
    /// <summary>
    /// Try to get an object of specific type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetObject<T>(in RefID id, out T element) where T : class, IWorldElement
    {
        if (_objects.TryGetValue(id, out var obj) && obj is T typed)
        {
            element = typed;
            return true;
        }
        element = null!;
        return false;
    }
    
    /// <summary>
    /// Check if an object exists with the given RefID.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsObject(in RefID id)
    {
        return _objects.ContainsKey(id);
    }
    
    #endregion
    
    #region Async Reference Resolution
    
    /// <summary>
    /// Request an object by RefID. If available, callback is invoked immediately.
    /// If not yet available, callback is invoked when object is registered.
    /// </summary>
    public void RequestObject(in RefID id, IWorldElementReceiver receiver)
    {
        if (id.IsNull)
            return;
        
        if (receiver == null)
            throw new ArgumentNullException(nameof(receiver));
        
        // Check if already available
        if (_objects.TryGetValue(id, out var element))
        {
            receiver.OnWorldElementAvailable(element);
            return;
        }
        
        // Queue for later
        if (!_pendingRequests.TryGetValue(id, out var list))
        {
            list = new List<IWorldElementReceiver>();
            _pendingRequests[id] = list;
        }
        list.Add(receiver);
    }
    
    /// <summary>
    /// Cancel a pending request.
    /// </summary>
    public void CancelRequest(in RefID id, IWorldElementReceiver receiver)
    {
        if (_pendingRequests.TryGetValue(id, out var list))
        {
            list.Remove(receiver);
            if (list.Count == 0)
            {
                _pendingRequests.Remove(id);
            }
        }
    }
    
    #endregion
    
    #region Allocation Block Management
    
    /// <summary>
    /// Begin an allocation block at a specific RefID.
    /// Used when receiving objects from network or loading from save.
    /// </summary>
    public void AllocationBlockBegin(in RefID id)
    {
        CheckAllocation();
        byte userByte = id.GetUserByte();
        ulong position = id.GetPosition();

        if (_currentAllocation.UserByte == userByte && _currentAllocation.Position == position)
        {
            _currentAllocation.NestedDepth++;
            return;
        }

        _allocationStack.Push(_currentAllocation);

        _currentAllocation = new AllocationContext
        {
            UserByte = userByte,
            Position = position,
            NestedDepth = 0
        };
    }
    
    /// <summary>
    /// End the current allocation block and restore previous context.
    /// </summary>
    public void AllocationBlockEnd()
    {
        if (_currentAllocation.NestedDepth > 0)
        {
            _currentAllocation.NestedDepth--;
            return;
        }

        if (_allocationStack.Count == 0)
        {
            throw new InvalidOperationException("Allocation stack is empty, cannot end block!");
        }
        
        _currentAllocation = _allocationStack.Pop();
    }
    
    // Allocation blocks as a using-scope, and the only way they should be opened from now on. A hand-written
    // Begin/End pair is SKIPPED by any exception thrown between the two, and the controller then keeps minting
    // in that context forever: one component throwing out of OnInit inside a local component attach left the
    // whole world allocating LOCAL ids, so every user root slot built afterwards was refused by User.Root with
    // "Cannot reference local targets from non-local reference". -xlinka
    public readonly struct AllocationScope : IDisposable
    {
        internal enum Kind : byte { None, Local, Block, Owned, Script }

        private readonly ReferenceController? _controller;
        private readonly Kind _kind;
        private readonly byte _userByte;

        internal AllocationScope(ReferenceController controller, Kind kind, byte userByte = 0)
        {
            _controller = controller;
            _kind = kind;
            _userByte = userByte;
        }

        public void Dispose()
        {
            if (_controller == null)
                return;

            switch (_kind)
            {
                case Kind.Local:
                    _controller.LocalAllocationBlockEnd();
                    break;
                case Kind.Block:
                    _controller.AllocationBlockEnd();
                    break;
                case Kind.Owned:
                    _controller.OwnedAllocationBlockEnd(_userByte);
                    break;
                case Kind.Script:
                    _controller.ScriptAllocationBlockEnd(_userByte);
                    break;
            }
        }
    }

    public AllocationScope LocalAllocationScope()
    {
        LocalAllocationBlockBegin();
        return new AllocationScope(this, AllocationScope.Kind.Local);
    }

    public AllocationScope AllocationScopeAt(in RefID id)
    {
        AllocationBlockBegin(id);
        return new AllocationScope(this, AllocationScope.Kind.Block);
    }

    public AllocationScope OwnedAllocationScope(byte userByte)
    {
        OwnedAllocationBlockBegin(userByte);
        return new AllocationScope(this, AllocationScope.Kind.Owned, userByte);
    }

    public void SetScriptFence(byte userByte, ulong position)
    {
        if (!RefIDConstants.IsValidUserByte(userByte))
            return;
        _scriptFence[userByte] = position;
        _scriptPosition[userByte] = position;
    }

    public ulong GetScriptFence(byte userByte) => _scriptFence[userByte];

    // Whether an id sits below its byte's script fence, where that byte's own user mints.
    public bool BelowScriptFence(in RefID id)
    {
        ulong fence = _scriptFence[id.GetUserByte()];
        return fence == 0 || id.GetPosition() < fence;
    }

    // On the host: everything minted inside lands in a user's script range, and the range moves past it
    // when the scope closes, whatever happened inside.
    public AllocationScope ScriptAllocationScope(byte userByte)
    {
        if (!RefIDConstants.IsValidUserByte(userByte) || _scriptFence[userByte] == 0)
            throw new InvalidOperationException($"user byte {userByte} has no script range");
        ulong position = _scriptPosition[userByte];
        if (position >= RefID.MaxPosition - RefIDConstants.USER_JOIN_HEADROOM)
            throw new InvalidOperationException($"user byte {userByte} has run out of script range");
        AllocationBlockBegin(RefID.Construct(userByte, position));
        return new AllocationScope(this, AllocationScope.Kind.Script, userByte);
    }

    private void ScriptAllocationBlockEnd(byte userByte)
    {
        if (_currentAllocation.UserByte == userByte && _currentAllocation.NestedDepth == 0
            && _currentAllocation.Position > _scriptPosition[userByte])
            _scriptPosition[userByte] = _currentAllocation.Position;
        AllocationBlockEnd();
    }

    // For an element that either carries an assigned id or belongs to a local parent; neither means it
    // allocates in whatever context the caller is already in, and the scope then does nothing. -xlinka
    public AllocationScope ElementAllocationScope(in RefID id, bool local)
    {
        if (id != RefID.Null)
            return AllocationScopeAt(id);
        if (local)
            return LocalAllocationScope();
        return default;
    }

    // Begin a local allocation block for non-networked objects. Prefer LocalAllocationScope. -xlinka
    public void LocalAllocationBlockBegin()
    {
        CheckAllocation();
        if (_currentAllocation.UserByte == RefIDConstants.LOCAL_BYTE)
        {
            _localAllocationDepth++;
            return;
        }

        _allocationStack.Push(_currentAllocation);
        _currentAllocation = new AllocationContext
        {
            UserByte = RefIDConstants.LOCAL_BYTE,
            Position = _localAllocationPosition,
            NestedDepth = 0
        };
        _localAllocationDepth = 1;
    }
    
    public void LocalAllocationBlockEnd()
    {
        if (_currentAllocation.UserByte != RefIDConstants.LOCAL_BYTE)
        {
            throw new InvalidOperationException("Not in local allocation block!");
        }

        if (_localAllocationDepth > 1)
        {
            _localAllocationDepth--;
            return;
        }

        _localAllocationDepth = 0;

        // Save local position for next local block
        _localAllocationPosition = _currentAllocation.Position;

        if (_allocationStack.Count == 0)
        {
            throw new InvalidOperationException("Allocation stack is empty!");
        }

        _currentAllocation = _allocationStack.Pop();
    }

    /// <summary>
    /// Open an allocation block in a specific user's networked namespace (their allocation byte). Unlike
    /// LocalAllocationBlockBegin (byte 254, never networked) this mints real user-byte RefIDs that replicate,
    /// so anything built here is OWNED by that user and visible to everyone. Position carries forward via the
    /// per-byte high-water mark so re-entry keeps minting fresh IDs. -xlinka
    /// </summary>
    public void OwnedAllocationBlockBegin(byte userByte)
    {
        CheckAllocation();
        if (!RefIDConstants.IsValidUserByte(userByte))
            throw new ArgumentOutOfRangeException(nameof(userByte), "Owned block requires a real user byte (1-253)");

        // Never into a context at or past the byte's script fence: that is the decode of something the
        // host made for this user's scripts, and minting on from there would take ids the host is about to.
        if (_currentAllocation.UserByte == userByte
            && (_scriptFence[userByte] == 0 || _currentAllocation.Position < _scriptFence[userByte]))
        {
            _currentAllocation.NestedDepth++;
            return;
        }

        _allocationStack.Push(_currentAllocation);
        _currentAllocation = new AllocationContext
        {
            UserByte = userByte,
            Position = OwnedStart(userByte),
            NestedDepth = 0
        };
    }

    // A user's machine mints its byte from two cursors that start at the same granted position: its default
    // context, and the owned block's own for a block opened under some other context. So an owned block starts
    // past every context minting that byte, and on closing moves them past what it took; otherwise the two hand
    // out the same id. Only below the byte's script fence: past it is the host's. -xlinka
    private ulong OwnedStart(byte userByte)
    {
        ulong start = GetOwnedPosition(userByte);
        foreach (var context in _allocationStack)
        {
            if (context.UserByte == userByte && BelowFence(userByte, context.Position) && context.Position > start)
                start = context.Position;
        }
        return start;
    }

    private void CatchUp(byte userByte, ulong reached)
    {
        if (!BelowFence(userByte, reached))
            return;
        if (_currentAllocation.UserByte == userByte && _currentAllocation.Position < reached)
            _currentAllocation.Position = reached;

        bool behind = false;
        foreach (var context in _allocationStack)
        {
            if (context.UserByte == userByte && context.Position < reached)
            {
                behind = true;
                break;
            }
        }
        if (!behind)
            return;
        // The contexts are values, so the stack is rebuilt with those moved. ToArray is top first.
        var contexts = _allocationStack.ToArray();
        _allocationStack.Clear();
        for (int i = contexts.Length - 1; i >= 0; i--)
        {
            var context = contexts[i];
            if (context.UserByte == userByte && context.Position < reached)
                context.Position = reached;
            _allocationStack.Push(context);
        }
    }

    private bool BelowFence(byte userByte, ulong position) => _scriptFence[userByte] == 0 || position < _scriptFence[userByte];

    /// <summary>
    /// End an owned allocation block and restore the previous context, saving the high-water position so a
    /// later block in the same byte resumes above it. -xlinka
    /// </summary>
    public void OwnedAllocationBlockEnd(byte userByte)
    {
        if (_currentAllocation.UserByte != userByte)
            throw new InvalidOperationException("Not in the matching owned allocation block!");

        if (_currentAllocation.NestedDepth > 0)
        {
            _currentAllocation.NestedDepth--;
            return;
        }

        ulong reached = _currentAllocation.Position;
        _ownedAllocationPosition[userByte] = reached;
        if (_allocationStack.Count == 0)
            throw new InvalidOperationException("Allocation stack is empty!");
        _currentAllocation = _allocationStack.Pop();
        CatchUp(userByte, reached);
    }

    // Highest position registered so far in a user byte, or 0 when the byte has never been used.
    public ulong GetLatestPosition(byte userByte) => _latestUserPosition[userByte];

    /// <summary>
    /// Seed the start position for a user's owned namespace from the join grant. Callers pass a BARE position
    /// (already GetPosition()-decoded), never a packed RefID - see the JoinGrant handler. -xlinka
    /// </summary>
    public void SetOwnedStartPosition(byte userByte, ulong position)
    {
        if (!RefIDConstants.IsValidUserByte(userByte))
            return;
        _ownedAllocationPosition[userByte] = position == 0 ? 1UL : position;
    }

    /// <summary>
    /// Allocate the next RefID from current context.
    /// </summary>
    public RefID AllocateID()
    {
        CheckAllocation();
        RefID id = RefID.Construct(_currentAllocation.UserByte, _currentAllocation.Position);
        _currentAllocation.Position++;
        return id;
    }
    
    /// <summary>
    /// Peek at the next RefID without allocating.
    /// </summary>
    public RefID PeekID()
    {
        CheckAllocation();
        return RefID.Construct(_currentAllocation.UserByte, _currentAllocation.Position);
    }
    
    /// <summary>
    /// Set the allocation context directly.
    /// Used by RefIDAllocator for user range management.
    /// </summary>
    public void SetAllocationContext(byte userByte, ulong position)
    {
        CheckAllocation();
        _currentAllocation = new AllocationContext
        {
            UserByte = userByte,
            Position = position,
            NestedDepth = 0
        };
    }
    
    /// <summary>
    /// Get current allocation user byte.
    /// </summary>
    public byte CurrentUserByte => _currentAllocation.UserByte;
    
    /// <summary>
    /// Get current allocation position.
    /// </summary>
    public ulong CurrentPosition => _currentAllocation.Position;
    
    /// <summary>
    /// Whether currently in a local allocation block.
    /// </summary>
    public bool IsInLocalAllocation => _currentAllocation.UserByte == RefIDConstants.LOCAL_BYTE;
    
    #endregion
    
    #region Trash Integration
    
    /// <summary>
    /// Try to retrieve an object from trash during sync.
    /// Used when receiving delete confirmations that need rollback.
    /// </summary>
    public IWorldElement TryRetrieveFromTrash(ulong tick, RefID id)
    {
        if (_trashedObjects.TryGetValue(id, out var entry) && entry.TrashTick <= tick)
        {
            _trashedObjects.Remove(id);
            return entry.Element;
        }
        return null!;
    }
    
    /// <summary>
    /// Move an object to trash (pending deletion confirmation).
    /// </summary>
    public void MoveToTrash(IWorldElement element, ulong tick)
    {
        if (element == null) return;
        
        _trashedObjects[element.ReferenceID] = new TrashEntry
        {
            Element = element,
            TrashTick = tick
        };
        
        UnregisterObject(element);
    }
    
    /// <summary>
    /// Permanently delete from trash.
    /// </summary>
    public void DeleteFromTrash(RefID id)
    {
        _trashedObjects.Remove(id);
    }
    
    /// <summary>
    /// Restore from trash (deletion rejected).
    /// </summary>
    public bool RestoreFromTrash(RefID id)
    {
        if (_trashedObjects.TryGetValue(id, out var entry))
        {
            _trashedObjects.Remove(id);
            RegisterObject(entry.Element);
            return true;
        }
        return false;
    }
    
    #endregion
    
    #region Cleanup
    
    /// <summary>
    /// Clear all pending requests (used during world shutdown).
    /// </summary>
    public void ClearPendingRequests()
    {
        _pendingRequests.Clear();
    }
    
    /// <summary>
    /// Reset the controller (used when resetting world state).
    /// </summary>
    public void Reset()
    {
        _objects.Clear();
        _pendingRequests.Clear();
        _trashedObjects.Clear();
        _allocationStack.Clear();
        _localAllocationPosition = 1;
        _localAllocationDepth = 0;
        for (int i = 0; i < _ownedAllocationPosition.Length; i++)
        {
            _ownedAllocationPosition[i] = 1;
            _latestUserPosition[i] = 0;
        }
        BlockAllocations = false;
        _allocationBlockCount = 0;
        IsDecodingBatch = false;

        _currentAllocation = new AllocationContext
        {
            UserByte = RefIDConstants.AUTHORITY_BYTE,
            Position = 1,
            NestedDepth = 0
        };
    }
    
    public void Dispose()
    {
        Reset();
    }
    
    #endregion

    private void CheckAllocation()
    {
        // Warn about allocations during OnAwake context
        // but don't throw - some components legitimately need to create sync members
        if (_allocationBlockCount > 0)
        {
            LumoraLogger.Warn(
                "RefID allocation during OnAwake context. This may indicate a component " +
                "creating new objects during initialization. Consider deferring to OnInit.");
        }

        // The hard block (BlockAllocations property) still throws
        if (BlockAllocations)
        {
            throw new InvalidOperationException(
                "New RefID allocations are currently blocked. Are you accidentally " +
                "creating new objects in the wrong context?");
        }
    }
    
    #region Internal Types
    
    private struct AllocationContext
    {
        public byte UserByte;
        public ulong Position;
        public int NestedDepth;
    }
    
    private struct TrashEntry
    {
        public IWorldElement Element;
        public ulong TrashTick;
    }
    
    #endregion
}

