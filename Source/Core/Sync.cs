using System;
using Godot;

namespace Aquamarine.Source.Core;

/// <summary>
/// Synchronized field that automatically replicates changes across the network.
/// 
/// </summary>
/// <typeparam name="T">The type of value to synchronize.</typeparam>
public class Sync<[MustBeVariant] T>
{
	private T _value;
	private IWorldElement _owner;
	private bool _isSyncing;

	/// <summary>
	/// Event triggered when the value changes.
	/// </summary>
	public event Action<T> OnChanged;

	/// <summary>
	/// The current value.
	/// Setting this will trigger network synchronization.
	/// </summary>
	public T Value
	{
		get => _value;
		set
		{
			if (Equals(_value, value)) return;

			var oldValue = _value;
			_value = value;

			// Trigger change event
			OnChanged?.Invoke(_value);

			// Mark owner as dirty for network sync (only if not currently syncing from network)
			if (!_isSyncing && _owner != null)
			{
				_owner.World?.MarkElementDirty(_owner);
			}
		}
	}

	/// <summary>
	/// The world element that owns this Sync field.
	/// </summary>
	public IWorldElement Owner => _owner;

	/// <summary>
	/// Whether this field has been modified since last sync.
	/// </summary>
	public bool IsDirty { get; internal set; }

	public Sync(IWorldElement owner, T defaultValue = default)
	{
		_owner = owner;
		_value = defaultValue;
		IsDirty = false;
	}

	/// <summary>
	/// Set the value without triggering change events or network sync.
	/// Used when receiving values from the network.
	/// </summary>
	internal void SetValueFromNetwork(T value)
	{
		_isSyncing = true;
		_value = value;
		OnChanged?.Invoke(_value);
		_isSyncing = false;
		IsDirty = false;
	}

	/// <summary>
	/// Encode this value for network transmission.
	/// </summary>
	public byte[] Encode()
	{
		return GodotSerializer.Serialize(_value);
	}

	/// <summary>
	/// Decode and set value from network data.
	/// </summary>
	public void Decode(byte[] data)
	{
		var value = GodotSerializer.Deserialize<T>(data);
		SetValueFromNetwork(value);
	}

	public static implicit operator T(Sync<T> sync) => sync.Value;

	public override string ToString() => _value?.ToString() ?? "null";
}

/// <summary>
/// Helper class for serializing values for network transmission.
/// </summary>
public static class GodotSerializer
{
	public static byte[] Serialize<[MustBeVariant] T>(T value)
	{
		// Use Godot's Variant system for serialization
		var variant = Variant.From(value);
		return GD.VarToBytes(variant);
	}

	public static T Deserialize<[MustBeVariant] T>(byte[] data)
	{
		var variant = GD.BytesToVar(data);
		return variant.As<T>();
	}
}
