using System;

namespace Lumora.Core;

/// <summary>
/// Synchronized reference to another world element.
/// Handles network synchronization and change tracking.
/// Implements IChangeable for reactive change tracking.
/// </summary>
public class SyncRef<T> : IChangeable where T : class, IWorldElement
{
    private T? _target;
    private IWorldElement _owner;
    private bool _isSyncing;

    /// <summary>
    /// Event triggered when the reference changes.
    /// </summary>
    public event Action<SyncRef<T>>? OnChanged;

    /// <summary>
    /// Event fired when this sync reference changes (IChangeable implementation).
    /// </summary>
    public event Action<IChangeable> Changed;

    /// <summary>
    /// The referenced target element.
    /// Setting this will trigger network synchronization.
    /// </summary>
    public T? Target
    {
        get => _target;
        set
        {
            if (_target == value) return;

            _target = value;
            IsDirty = true;

            // Trigger change event
            OnChanged?.Invoke(this);

            // Mark owner as dirty for network sync
            if (!_isSyncing && _owner != null)
            {
                _owner.World?.MarkElementDirty(_owner);

                // Notify the parent component that this sync reference changed
                if (_owner is Component component)
                {
                    component.NotifyChanged();
                }

                // Fire the IChangeable Changed event
                Changed?.Invoke(this);
            }
        }
    }

    /// <summary>
    /// The world element that owns this SyncRef.
    /// </summary>
    public IWorldElement Owner => _owner;

    /// <summary>
    /// Whether this reference has been modified since last sync.
    /// </summary>
    public bool IsDirty { get; internal set; }

    public SyncRef(IWorldElement owner, T? defaultTarget = null)
    {
        _owner = owner;
        _target = defaultTarget;
        IsDirty = false;
    }

    /// <summary>
    /// Set the target without triggering change events or network sync.
    /// Used when receiving values from the network.
    /// </summary>
    internal void SetTargetFromNetwork(T? target)
    {
        _isSyncing = true;
        _target = target;
        OnChanged?.Invoke(this);
        _isSyncing = false;
        IsDirty = false;
    }

    /// <summary>
    /// Check if reference was changed and clear the dirty flag.
    /// </summary>
    public bool GetWasChangedAndClear()
    {
        bool wasChanged = IsDirty;
        IsDirty = false;
        return wasChanged;
    }

    /// <summary>
    /// Implicit conversion to target for convenience.
    /// </summary>
    public static implicit operator T?(SyncRef<T> syncRef) => syncRef?._target;

    public override string ToString() => $"SyncRef<{typeof(T).Name}>({_target?.ToString() ?? "null"})";

    // IWorldElement implementation (forwarded to owner)

    /// <summary>
    /// The World this reference belongs to.
    /// </summary>
    public World World => _owner?.World;

    /// <summary>
    /// Unique reference ID for this reference within the world.
    /// </summary>
    public ulong RefID => _owner?.RefID ?? 0;

    /// <summary>
    /// Whether this reference has been destroyed.
    /// </summary>
    public bool IsDestroyed => _owner?.IsDestroyed ?? false;

    /// <summary>
    /// Whether this reference has been initialized.
    /// </summary>
    public bool IsInitialized => _owner?.IsInitialized ?? false;

    /// <summary>
    /// Destroy this reference (cannot be destroyed directly, owner must be destroyed).
    /// </summary>
    public void Destroy()
    {
        // SyncRef fields cannot be destroyed directly
        // They are destroyed when their owner is destroyed
    }
}
