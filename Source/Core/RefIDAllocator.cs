using System;
using AquaLogger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Core;

/// <summary>
/// Manages RefID allocation to prevent conflicts between users.
/// Uses byte-based partitioning of the ID space.
///
/// RefID Structure (64-bit):
/// - Byte 0 (MSB): User allocation byte (0-254 for users, 255 for authority)
/// - Bytes 1-7: Sequential counter within user's allocation
///
/// This gives each user ~72 quadrillion IDs (2^56).
/// </summary>
public class RefIDAllocator
{
	private const byte AUTHORITY_BYTE = 255;
	private const byte MAX_USER_BYTE = 254;

	private byte _nextUserByte = 0;
	private readonly World _world;

	public RefIDAllocator(World world)
	{
		_world = world;
	}

	/// <summary>
	/// Allocate an ID range for a new user.
	/// Returns (start, end) tuple.
	/// </summary>
	public (ulong start, ulong end) AllocateUserIDRange()
	{
		if (_nextUserByte > MAX_USER_BYTE)
		{
			throw new InvalidOperationException("Ran out of user allocation bytes! Maximum 255 users supported.");
		}

		byte userByte = _nextUserByte++;

		ulong start = CalculateIDRangeStart(userByte);
		ulong end = CalculateIDRangeEnd(userByte);

		AquaLogger.Log($"Allocated ID range for user byte {userByte}: {start:X16} - {end:X16}");

		return (start, end);
	}

	/// <summary>
	/// Get the authority's ID range (always uses byte 255).
	/// </summary>
	public (ulong start, ulong end) GetAuthorityIDRange()
	{
		ulong start = CalculateIDRangeStart(AUTHORITY_BYTE);
		ulong end = CalculateIDRangeEnd(AUTHORITY_BYTE);

		return (start, end);
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
	/// Check if a RefID belongs to the authority.
	/// </summary>
	public static bool IsAuthorityID(ulong refID)
	{
		return GetUserByteFromRefID(refID) == AUTHORITY_BYTE;
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
		_nextUserByte = 0;
		AquaLogger.Log("RefID allocator reset");
	}

	/// <summary>
	/// Get the number of user bytes already allocated.
	/// </summary>
	public int GetAllocatedUserCount()
	{
		return _nextUserByte;
	}

	/// <summary>
	/// Get the maximum number of users supported.
	/// </summary>
	public int GetMaxUserCount()
	{
		return MAX_USER_BYTE + 1; // 255 users (0-254)
	}

	/// <summary>
	/// Format a RefID as a human-readable string showing byte structure.
	/// Example: "User[042]:0000000000001A3F" (User byte 42, counter 0x1A3F)
	/// </summary>
	public static string FormatRefID(ulong refID)
	{
		byte userByte = GetUserByteFromRefID(refID);
		ulong counter = refID & 0x00FFFFFFFFFFFFFFUL;

		string userLabel = userByte == AUTHORITY_BYTE ? "Auth" : $"User[{userByte:D3}]";
		return $"{userLabel}:{counter:X14}";
	}
}
