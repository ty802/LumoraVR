using System;
using System.Collections.Generic;
using AquaLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core;

/// <summary>
/// Manages RefID allocation to prevent conflicts between users.
/// RefID Structure (64-bit):
/// - Byte 0 (MSB): User allocation byte (0=Host, 1-253=Users, 254=Local, 255=Reserved)
/// - Bytes 1-7: Sequential counter within allocation
/// </summary>
public class RefIDAllocator
{
	private const byte AUTHORITY_BYTE = 0;      // Host/authority uses byte 0
	private const byte LOCAL_BYTE = 255;        // Local-only objects (not networked)
	private const byte MAX_USER_BYTE = 253;     // Max user allocation byte
	private const byte RESERVED_BYTE = 254;     // Reserved for future use

	/// <summary>
	/// Allocation context for nested allocation blocks.
	/// </summary>
	private struct AllocationContext
	{
		public byte UserByte;
		public ulong Position;
		public ushort NestedLevels;
	}

	private byte _nextUserByte = 1; // Start at 1 (0 reserved for authority)
	private readonly World _world;

	// Allocation context stack (for nested allocations)
	private AllocationContext _currentContext;
	private readonly Stack<AllocationContext> _contextStack = new();

	// Track latest position for each user byte (for loading/sync)
	private readonly ulong[] _latestUserPosition = new ulong[256];

	// Local allocation position (separate from user allocations)
	private ulong _localAllocationPosition = 1;

	public RefIDAllocator(World world)
	{
		_world = world;

		// Initialize default context (authority)
		_currentContext = new AllocationContext
		{
			UserByte = AUTHORITY_BYTE,
			Position = 1,
			NestedLevels = 0
		};

		// Initialize position tracking
		for (int i = 0; i < 256; i++)
		{
			_latestUserPosition[i] = 1;
		}
	}

	/// <summary>
	/// Allocate an ID range for a new user.
	/// Returns (start, end) tuple.
	/// </summary>
	public (ulong start, ulong end) AllocateUserIDRange()
	{
		if (_nextUserByte > MAX_USER_BYTE)
		{
			throw new InvalidOperationException($"Ran out of user allocation bytes! Maximum {MAX_USER_BYTE} users supported.");
		}

		byte userByte = _nextUserByte++;

		ulong start = CalculateIDRangeStart(userByte);
		ulong end = CalculateIDRangeEnd(userByte);

		AquaLogger.Log($"Allocated ID range for user byte {userByte}: {start:X16} - {end:X16}");

		return (start, end);
	}

	/// <summary>
	/// Get the authority's ID range (always uses byte 0).
	/// </summary>
	public (ulong start, ulong end) GetAuthorityIDRange()
	{
		ulong start = CalculateIDRangeStart(AUTHORITY_BYTE);
		ulong end = CalculateIDRangeEnd(AUTHORITY_BYTE);

		return (start, end);
	}

	/// <summary>
	/// Get the local allocation ID range (byte 254).
	/// </summary>
	public (ulong start, ulong end) GetLocalIDRange()
	{
		ulong start = CalculateIDRangeStart(LOCAL_BYTE);
		ulong end = CalculateIDRangeEnd(LOCAL_BYTE);

		return (start, end);
	}

	/// <summary>
	/// Allocate a RefID from the current allocation context.
	/// </summary>
	public ulong AllocateID()
	{
		ulong id = ConstructRefID(_currentContext.Position, _currentContext.UserByte);
		_currentContext.Position++;

		// Track latest position
		UpdateLatestPosition(id);

		return id;
	}

	/// <summary>
	/// Peek at the next RefID without allocating it.
	/// </summary>
	public ulong PeekID()
	{
		return ConstructRefID(_currentContext.Position, _currentContext.UserByte);
	}

	/// <summary>
	/// Begin a local allocation block for UI and other local-only objects.
	/// </summary>
	public void LocalAllocationBlockBegin()
	{
		// If already in local allocation, just increment nesting
		if (_currentContext.UserByte == LOCAL_BYTE)
		{
			_currentContext.NestedLevels++;
			return;
		}

		// Push current context and switch to local
		PushAllocationContext();
		_currentContext.UserByte = LOCAL_BYTE;
		_currentContext.Position = _localAllocationPosition;
		_currentContext.NestedLevels = 0;
	}

	/// <summary>
	/// End local allocation block.
	/// </summary>
	public void LocalAllocationBlockEnd()
	{
		if (_currentContext.UserByte != LOCAL_BYTE)
		{
			throw new InvalidOperationException("Not currently in local allocation block!");
		}

		if (_currentContext.NestedLevels == 0)
		{
			// Save local position and restore previous context
			_localAllocationPosition = _currentContext.Position;
			PopAllocationContext();
		}
		else
		{
			_currentContext.NestedLevels--;
		}
	}

	/// <summary>
	/// Begin an allocation block for a specific user.
	/// Used when loading objects or receiving network updates.
	/// </summary>
	public void AllocationBlockBegin(byte userByte, ulong position)
	{
		// If same user and position, just increment nesting
		if (_currentContext.UserByte == userByte && _currentContext.Position == position)
		{
			_currentContext.NestedLevels++;
			return;
		}

		// Push current context and switch to new allocation
		PushAllocationContext();
		_currentContext.UserByte = userByte;
		_currentContext.Position = position;
		_currentContext.NestedLevels = 0;
	}

	/// <summary>
	/// End allocation block.
	/// </summary>
	public void AllocationBlockEnd()
	{
		if (_currentContext.NestedLevels == 0)
		{
			PopAllocationContext();
		}
		else
		{
			_currentContext.NestedLevels--;
		}
	}

	/// <summary>
	/// Push current allocation context onto stack.
	/// </summary>
	private void PushAllocationContext()
	{
		_contextStack.Push(_currentContext);
	}

	/// <summary>
	/// Pop allocation context from stack.
	/// </summary>
	private void PopAllocationContext()
	{
		if (_contextStack.Count == 0)
		{
			throw new InvalidOperationException("Allocation context stack is empty!");
		}

		_currentContext = _contextStack.Pop();
	}

	/// <summary>
	/// Update latest position tracking for a user byte.
	/// </summary>
	private void UpdateLatestPosition(ulong refID)
	{
		byte userByte = GetUserByteFromRefID(refID);
		ulong position = GetPositionFromRefID(refID);

		if (position > _latestUserPosition[userByte])
		{
			_latestUserPosition[userByte] = position;
		}
	}

	/// <summary>
	/// Construct a RefID from position and user byte.
	/// </summary>
	private static ulong ConstructRefID(ulong position, byte userByte)
	{
		return ((ulong)userByte << 56) | (position & 0x00FFFFFFFFFFFFFFUL);
	}

	/// <summary>
	/// Calculate the starting ID for a given user byte.
	/// </summary>
	private static ulong CalculateIDRangeStart(byte userByte)
	{
		// User byte goes in the most significant byte
		// Format: [user_byte][00][00][00][00][00][00][01]
		// Start at 1 to avoid 0 (which is often used as "invalid")
		return ((ulong)userByte << 56) | 0x0000000000000001UL;
	}

	/// <summary>
	/// Calculate the ending ID (exclusive) for a given user byte.
	/// </summary>
	private static ulong CalculateIDRangeEnd(byte userByte)
	{
		// End is the start of the next user's range
		// Format: [next_user_byte][00][00][00][00][00][00][00]
		return ((ulong)(userByte + 1) << 56);
	}

	/// <summary>
	/// Extract the user byte from a RefID.
	/// </summary>
	public static byte GetUserByteFromRefID(ulong refID)
	{
		return (byte)(refID >> 56);
	}

	/// <summary>
	/// Extract the position (counter) from a RefID.
	/// </summary>
	public static ulong GetPositionFromRefID(ulong refID)
	{
		return refID & 0x00FFFFFFFFFFFFFFUL;
	}

	/// <summary>
	/// Check if a RefID belongs to the authority.
	/// </summary>
	public static bool IsAuthorityID(ulong refID)
	{
		return GetUserByteFromRefID(refID) == AUTHORITY_BYTE;
	}

	/// <summary>
	/// Check if a RefID is local-only.
	/// </summary>
	public static bool IsLocalID(ulong refID)
	{
		return GetUserByteFromRefID(refID) == LOCAL_BYTE;
	}

	/// <summary>
	/// Check if a RefID belongs to a specific user byte.
	/// </summary>
	public static bool BelongsToUser(ulong refID, byte userByte)
	{
		return GetUserByteFromRefID(refID) == userByte;
	}

	/// <summary>
	/// Validate that a RefID is within the expected range.
	/// </summary>
	public static bool IsValidRefID(ulong refID, ulong rangeStart, ulong rangeEnd)
	{
		return refID >= rangeStart && refID < rangeEnd;
	}

	/// <summary>
	/// Reset the allocator (used when starting a new session).
	/// </summary>
	public void Reset()
	{
		_nextUserByte = 1; // Start at 1 (0 reserved for authority)
		_localAllocationPosition = 1;
		_contextStack.Clear();

		// Reset to authority context
		_currentContext = new AllocationContext
		{
			UserByte = AUTHORITY_BYTE,
			Position = 1,
			NestedLevels = 0
		};

		// Reset position tracking
		for (int i = 0; i < 256; i++)
		{
			_latestUserPosition[i] = 1;
		}

		AquaLogger.Log("RefID allocator reset");
	}

	/// <summary>
	/// Get the number of user bytes already allocated.
	/// </summary>
	public int GetAllocatedUserCount()
	{
		return _nextUserByte - 1; // Subtract 1 because we start at 1
	}

	/// <summary>
	/// Get the maximum number of users supported.
	/// </summary>
	public int GetMaxUserCount()
	{
		return MAX_USER_BYTE; // 253 users (1-253, 0 is authority)
	}

	/// <summary>
	/// Format a RefID as a human-readable string showing byte structure.
	/// Example: "Auth:0000000000001A3F" or "User[042]:0000000000001A3F" or "Local:0000000000001A3F"
	/// </summary>
	public static string FormatRefID(ulong refID)
	{
		byte userByte = GetUserByteFromRefID(refID);
		ulong counter = refID & 0x00FFFFFFFFFFFFFFUL;

		string userLabel = userByte switch
		{
			AUTHORITY_BYTE => "Auth",
			LOCAL_BYTE => "Local",
			RESERVED_BYTE => "Reserved",
			_ => $"User[{userByte:D3}]"
		};

		return $"{userLabel}:{counter:X14}";
	}

	/// <summary>
	/// Get current allocation context info (for debugging).
	/// </summary>
	public string GetCurrentContextInfo()
	{
		return $"User:{_currentContext.UserByte} Pos:{_currentContext.Position} Nested:{_currentContext.NestedLevels}";
	}
}
