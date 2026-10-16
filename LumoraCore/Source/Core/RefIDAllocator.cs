// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core;

// Each user gets one byte (1-253) as its ID domain; authority is byte 0, local objects byte 254.
public class RefIDAllocator
{
    private byte _nextUserByte = RefIDConstants.FIRST_USER_BYTE;
    private readonly World _world;
    private readonly object _lock = new object();

    // Track latest position for each user byte (for save/load)
    private readonly ulong[] _latestUserPosition = new ulong[256];

    private readonly Dictionary<byte, User> _userByteToUser = new Dictionary<byte, User>();

    // Bytes from users who left, ready to be handed out again so a long session with lots of
    // join/leave churn doesn't burn through the 253 slots and faceplant. Lowest free byte first. -xlinka
    private readonly SortedSet<byte> _freedUserBytes = new SortedSet<byte>();

    private long _totalAllocations;
    private long _authorityAllocations;
    private long _localAllocations;

    public RefIDAllocator(World world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));

        for (int i = 0; i < 256; i++)
        {
            _latestUserPosition[i] = 1;
        }
    }

    #region Allocation

    public RefIDRange AllocateUserIDRange(User user = null!)
    {
        lock (_lock)
        {
            byte userByte;
            if (_freedUserBytes.Count > 0)
            {
                // Reuse a byte from someone who left; ResumePosition below starts the range past
                // whatever they minted rather than replaying their positions.
                userByte = _freedUserBytes.Min;
                _freedUserBytes.Remove(userByte);
            }
            else
            {
                if (_nextUserByte > RefIDConstants.MAX_USER_BYTE)
                {
                    throw new InvalidOperationException(
                        $"Ran out of user allocation bytes! Maximum {RefIDConstants.MAX_USER_BYTE} users supported.");
                }

                userByte = _nextUserByte++;
            }

            var range = new RefIDRange(
                RefID.Construct(userByte, ResumePosition(userByte)),
                RefID.GetRangeEndExclusive(userByte));

            if (user != null)
            {
                _userByteToUser[userByte] = user;
            }

            LumoraLogger.Debug($"Allocated ID range for user byte {userByte}: {range}");

            return range;
        }
    }

    // Where a (re)joiner on this byte starts. A fresh byte starts at 1; a recycled one starts past
    // everything its previous occupant registered, plus the block the joiner allocates client side
    // (RefIDConstants.USER_JOIN_HEADROOM) so the next reuse clears that too.
    //
    // The high-water mark comes from the reference controller, not from this class: a joiner mints its
    // own IDs on its own machine and the host only ever sees them as registrations. Growth is LINEAR in
    // reuses - the position is carried as a position and never re-packed - which is the part the old
    // packed-vs-position confusion in the join handler got wrong. -xlinka
    private ulong ResumePosition(byte userByte)
    {
        ulong seen = System.Math.Max(
            _world.ReferenceController?.GetLatestPosition(userByte) ?? 0UL,
            _latestUserPosition[userByte]);

        if (seen <= 1)
        {
            return 1;
        }

        ulong resume = seen + 1;
        // 2^56 positions per byte: reaching this needs a session that recycled the same byte for
        // longer than any session runs. Clamping beats wrapping into IDs already handed out.
        if (resume > RefID.MaxPosition - RefIDConstants.USER_JOIN_HEADROOM)
        {
            LumoraLogger.Warn($"RefIDAllocator: user byte {userByte} is out of position space, restarting it at 1.");
            resume = 1;
        }

        _latestUserPosition[userByte] = resume;
        return resume;
    }

    public (RefID start, RefID end) AllocateUserIDRangeTuple(User user = null!)
    {
        var range = AllocateUserIDRange(user);
        return (range.Start, range.End);
    }

    public RefIDRange GetAuthorityIDRange()
    {
        return RefIDRange.Authority;
    }

    public RefIDRange GetLocalIDRange()
    {
        return RefIDRange.Local;
    }

    // The byte goes back to the free pool; its position counter is left alone on purpose so a reused
    // byte keeps minting fresh IDs instead of replaying old ones.
    public void ReleaseUserAllocation(byte userByte)
    {
        lock (_lock)
        {
            _userByteToUser.Remove(userByte);

            // Only recycle bytes we actually handed out, and don't double-add the same one. -xlinka
            if (RefIDConstants.IsValidUserByte(userByte) && userByte < _nextUserByte)
            {
                _freedUserBytes.Add(userByte);
            }

            LumoraLogger.Debug($"Released allocation for user byte {userByte}");
        }
    }

    #endregion

    #region Position Tracking

    public void UpdateLatestPosition(RefID id)
    {
        byte userByte = id.GetUserByte();
        ulong position = id.GetPosition();

        lock (_lock)
        {
            if (position > _latestUserPosition[userByte])
            {
                _latestUserPosition[userByte] = position;
            }

            _totalAllocations++;
            if (userByte == RefIDConstants.AUTHORITY_BYTE)
                _authorityAllocations++;
            else if (userByte == RefIDConstants.LOCAL_BYTE)
                _localAllocations++;
        }
    }

    public ulong GetLatestPosition(byte userByte)
    {
        lock (_lock)
        {
            return _latestUserPosition[userByte];
        }
    }

    public void SetLatestPosition(byte userByte, ulong position)
    {
        lock (_lock)
        {
            _latestUserPosition[userByte] = position;
        }
    }

    public RefID PeekNextID(byte userByte)
    {
        lock (_lock)
        {
            return RefID.Construct(userByte, _latestUserPosition[userByte] + 1);
        }
    }

    #endregion

    #region Query Methods

    public int AllocatedUserCount
    {
        get
        {
            lock (_lock)
            {
                // High-water mark minus the bytes sitting in the free pool is who is actually live. -xlinka
                return (_nextUserByte - RefIDConstants.FIRST_USER_BYTE) - _freedUserBytes.Count;
            }
        }
    }

    public int MaxUserCount => RefIDConstants.MAX_USERS;

    public int RemainingUserSlots => MaxUserCount - AllocatedUserCount;

    public bool CanAllocateMoreUsers => RemainingUserSlots > 0;

    public User GetUserForByte(byte userByte)
    {
        lock (_lock)
        {
            return (_userByteToUser.TryGetValue(userByte, out var user) ? user : null) ?? null!;
        }
    }

    public User GetOwner(RefID refId)
    {
        if (refId.IsNull || refId.IsLocalID || refId.IsAuthorityID)
            return null!;

        return GetUserForByte(refId.GetUserByte());
    }

    public bool BelongsToUser(RefID refId, User user)
    {
        if (user == null || refId.IsNull)
            return false;

        byte userByte = refId.GetUserByte();
        lock (_lock)
        {
            return _userByteToUser.TryGetValue(userByte, out var owner) && owner == user;
        }
    }

    public ulong GetAllocationCount(byte userByte)
    {
        lock (_lock)
        {
            return _latestUserPosition[userByte];
        }
    }

    public double GetUtilization(byte userByte)
    {
        lock (_lock)
        {
            return (double)_latestUserPosition[userByte] / RefID.MaxPosition * 100.0;
        }
    }

    #endregion

    #region Statistics

    public long TotalAllocations
    {
        get { lock (_lock) return _totalAllocations; }
    }

    public long AuthorityAllocations
    {
        get { lock (_lock) return _authorityAllocations; }
    }

    public long LocalAllocations
    {
        get { lock (_lock) return _localAllocations; }
    }

    public string GetDiagnostics()
    {
        lock (_lock)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"RefIDAllocator Diagnostics:");
            sb.AppendLine($"  Users allocated: {AllocatedUserCount}/{MaxUserCount}");
            sb.AppendLine($"  Total allocations: {_totalAllocations}");
            sb.AppendLine($"  Authority allocations: {_authorityAllocations}");
            sb.AppendLine($"  Local allocations: {_localAllocations}");
            sb.AppendLine($"  Authority position: {_latestUserPosition[RefIDConstants.AUTHORITY_BYTE]}");
            sb.AppendLine($"  Local position: {_latestUserPosition[RefIDConstants.LOCAL_BYTE]}");

            foreach (var kvp in _userByteToUser)
            {
                sb.AppendLine($"  User byte {kvp.Key}: {kvp.Value?.UserName ?? "Unknown"} (pos: {_latestUserPosition[kvp.Key]})");
            }

            return sb.ToString();
        }
    }

    #endregion

    #region Lifecycle

    public void Reset()
    {
        lock (_lock)
        {
            _nextUserByte = RefIDConstants.FIRST_USER_BYTE;
            _userByteToUser.Clear();
            _freedUserBytes.Clear();
            _totalAllocations = 0;
            _authorityAllocations = 0;
            _localAllocations = 0;

            for (int i = 0; i < 256; i++)
            {
                _latestUserPosition[i] = 1;
            }

            LumoraLogger.Debug("RefIDAllocator reset");
        }
    }

    #endregion
}

