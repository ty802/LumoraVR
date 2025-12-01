using System;
using System.Collections.Generic;
using System.IO;
using AquaLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking.Sync;

/// <summary>
/// Base class for all synchronizable elements.
/// </summary>
public abstract class SyncElement : IWorldElement, IDisposable
{
	// Flags packed into single byte for efficiency
	protected byte _flags;

	// Flag bit positions
	private const byte FLAG_SYNC_DIRTY = 0x01;
	private const byte FLAG_DISPOSED = 0x02;
	private const byte FLAG_LOCAL_ELEMENT = 0x04;
	private const byte FLAG_LOADING = 0x08;
	private const byte FLAG_NON_PERSISTENT = 0x10;

	/// <summary>
	/// The World this element belongs to.
	/// </summary>
	public World World { get; protected set; }

	/// <summary>
	/// Unique reference ID for network synchronization.
	/// </summary>
	public ulong RefID { get; protected set; }

	/// <summary>
	/// Alias for RefID.
	/// </summary>
	public ulong ReferenceID => RefID;

	/// <summary>
	/// Whether this element needs to be synchronized.
	/// </summary>
	public bool IsSyncDirty
	{
		get => (_flags & FLAG_SYNC_DIRTY) != 0;
		protected set
		{
			if (value)
				_flags |= FLAG_SYNC_DIRTY;
			else
				_flags &= unchecked((byte)~FLAG_SYNC_DIRTY);
		}
	}

	/// <summary>
	/// Whether this element has been disposed.
	/// </summary>
	public bool IsDisposed
	{
		get => (_flags & FLAG_DISPOSED) != 0;
		protected set
		{
			if (value)
				_flags |= FLAG_DISPOSED;
			else
				_flags &= unchecked((byte)~FLAG_DISPOSED);
		}
	}

	/// <summary>
	/// Whether this is a local-only element (not synchronized).
	/// </summary>
	public bool IsLocalElement
	{
		get => (_flags & FLAG_LOCAL_ELEMENT) != 0;
		protected set
		{
			if (value)
				_flags |= FLAG_LOCAL_ELEMENT;
			else
				_flags &= unchecked((byte)~FLAG_LOCAL_ELEMENT);
		}
	}

	/// <summary>
	/// Whether this element is currently being loaded from network/save.
	/// </summary>
	public bool IsLoading
	{
		get => (_flags & FLAG_LOADING) != 0;
		protected set
		{
			if (value)
				_flags |= FLAG_LOADING;
			else
				_flags &= unchecked((byte)~FLAG_LOADING);
		}
	}

	/// <summary>
	/// Whether this element should not be persisted to save files.
	/// </summary>
	public bool NonPersistent
	{
		get => (_flags & FLAG_NON_PERSISTENT) != 0;
		protected set
		{
			if (value)
				_flags |= FLAG_NON_PERSISTENT;
			else
				_flags &= unchecked((byte)~FLAG_NON_PERSISTENT);
		}
	}

	public bool IsDestroyed => IsDisposed;
	public bool IsInitialized => World != null;

	/// <summary>
	/// Mark this element as needing synchronization.
	/// Called when the element's value changes.
	/// </summary>
	public void InvalidateSyncElement()
	{
		if (IsLocalElement || IsDisposed || IsSyncDirty)
			return;

		if (World?.SyncController == null)
			return;

		IsSyncDirty = true;
		World.SyncController.AddDirtySyncElement(this);
		AquaLogger.Debug($"SyncElement: Invalidated {RefID}");
	}

	/// <summary>
	/// Mark this element as non-persistent (won't be saved).
	/// </summary>
	public void MarkNonPersistent()
	{
		NonPersistent = true;
	}

	/// <summary>
	/// Mark this element as local-only (won't be synchronized).
	/// </summary>
	public void MarkLocalElement()
	{
		IsLocalElement = true;
	}

	// ===== ENCODING/DECODING =====

	/// <summary>
	/// Encode full state for new users or corrections.
	/// </summary>
	public virtual void EncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage)
	{
		if (!World.IsAuthority)
			throw new InvalidOperationException("Non-authority shouldn't do a full encode!");
		if (IsSyncDirty)
			throw new InvalidOperationException("Cannot do a full encode on a dirty element!");

		InternalEncodeFull(writer, outboundMessage);
	}

	/// <summary>
	/// Decode full state from authority.
	/// </summary>
	public virtual void DecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage)
	{
		if (World.IsAuthority)
			throw new InvalidOperationException("Authority shouldn't do a full decode!");

		IsLoading = true;
		InternalDecodeFull(reader, inboundMessage);
		InternalClearDirty();
		IsLoading = false;
	}

	/// <summary>
	/// Encode delta (changes only) for regular sync.
	/// </summary>
	public virtual void EncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage)
	{
		InternalEncodeDelta(writer, outboundMessage);
		IsSyncDirty = false;
		InternalClearDirty();
	}

	/// <summary>
	/// Decode delta from network.
	/// </summary>
	public virtual void DecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage)
	{
		if (IsSyncDirty)
			throw new InvalidOperationException("Cannot apply delta to a dirty element!");

		IsLoading = true;
		InternalDecodeDelta(reader, inboundMessage);
		IsLoading = false;
	}

	// ===== ABSTRACT METHODS - Implement in derived classes =====

	protected abstract void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage);
	protected abstract void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage);
	protected abstract void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage);
	protected abstract void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage);
	protected abstract void InternalClearDirty();

	// ===== VALIDATION (for authority conflict resolution) =====

	/// <summary>
	/// Validate incoming delta message.
	/// </summary>
	public virtual MessageValidity Validate(BinaryMessageBatch syncMessage, BinaryReader reader, List<ValidationRule> rules)
	{
		// Default: accept all messages
		return MessageValidity.Valid;
	}

	/// <summary>
	/// Called when this element's change was rejected/conflicted.
	/// </summary>
	public virtual void Invalidate()
	{
		// Mark for re-sync
		InvalidateSyncElement();
	}

	/// <summary>
	/// Called when authority confirms our change.
	/// </summary>
	public virtual void Confirm(ulong confirmSyncTime)
	{
		// Override if needed
	}

	public virtual void Dispose()
	{
		IsDisposed = true;
		World = null;
	}

	public void Destroy()
	{
		Dispose();
	}
}

/// <summary>
/// Message validity result from validation.
/// </summary>
public enum MessageValidity
{
	Valid,
	Invalid,
	Conflict
}

/// <summary>
/// Validation rule for conflict detection.
/// </summary>
public class ValidationRule
{
	public ulong OtherMessage { get; set; }
	public bool MustExist { get; set; }
	public Func<BinaryReader, bool> CustomValidation { get; set; }
}
