using System;
using System.IO;
using AquaLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking.Sync;

/// <summary>
/// Generic synchronized property.
/// Automatically tracked for network replication.
/// 
/// </summary>
public class Sync<T> : ISyncMember
{
    private T _value;
    private ulong _version;
    private bool _isDirty;

    public int MemberIndex { get; set; } = -1;
    public string Name { get; set; } = string.Empty;
    public ulong Version
    {
        get => _version;
        set => _version = value;
    }

    public bool IsDirty
    {
        get => _isDirty;
        set => _isDirty = value;
    }

    /// <summary>
    /// Event fired when value changes.
    /// </summary>
    public event Action<T> OnValueChanged;

    /// <summary>
    /// The synchronized value.
    /// Setting this will mark the member as dirty.
    /// </summary>
    public T Value
    {
        get => _value;
        set
        {
            if (!Coder<T>.Equals(_value, value))
            {
                _value = value;
                _version++;
                _isDirty = true;
                OnValueChanged?.Invoke(value);
            }
        }
    }

    /// <summary>
    /// Force set the value without checking equality.
    /// Used for initial setup or forced updates.
    /// </summary>
    public void ForceSet(T value)
    {
        _value = value;
        _version++;
        _isDirty = true;
        OnValueChanged?.Invoke(value);
    }

    /// <summary>
    /// Set the value without triggering dirty flag.
    /// Used when receiving network updates.
    /// </summary>
    internal void SetValueFromNetwork(T value)
    {
        if (!Coder<T>.Equals(_value, value))
        {
            _value = value;
            _version++;
            _isDirty = false; // Don't mark dirty for network updates
            OnValueChanged?.Invoke(value);
        }
    }

    public void Encode(BinaryWriter writer)
    {
        try
        {
            Coder<T>.Encode(writer, _value);
        }
        catch (Exception ex)
        {
            AquaLogger.Error($"Failed to encode {Name}: {ex.Message}");
        }
    }

    public void Decode(BinaryReader reader)
    {
        try
        {
            T newValue = Coder<T>.Decode(reader);
            SetValueFromNetwork(newValue);
        }
        catch (Exception ex)
        {
            AquaLogger.Error($"Failed to decode {Name}: {ex.Message}");
        }
    }

    public object GetValueAsObject()
    {
        return _value;
    }

    // Implicit conversion operator for easier usage
    public static implicit operator T(Sync<T> sync)
    {
        return sync != null ? sync.Value : default(T);
    }

    public Sync()
    {
        _value = default(T);
        _version = 0;
        _isDirty = false;
    }

    public Sync(T initialValue)
    {
        _value = initialValue;
        _version = 0;
        _isDirty = false;
    }

    public override string ToString()
    {
        return _value?.ToString() ?? "null";
    }
}
