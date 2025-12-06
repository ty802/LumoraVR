using System;
using System.Collections.Generic;
using System.Linq;
using Lumora.Core.Math;

namespace Lumora.Core;

/// <summary>
/// A Slot is a container for Components and other Slots.
/// Forms a hierarchical structure for managing transforms.
/// </summary>
public class Slot : IImplementable<IHook<Slot>>
{
    private readonly List<Slot> _children = new();
    private readonly List<Component> _components = new();
    private Slot _parent;
    private bool _isDestroyed;

    // Platform-agnostic transform state
    private float3 _position = float3.Zero;
    private floatQ _rotation = floatQ.Identity;
    private float3 _scale = float3.One;
    private bool _visible = true;
    private string _name = "Slot";

    // Transform caching with dirty flags
    // Dirty flags: bit 0=TRS, bit 1=LocalToWorld, bit 2=WorldToLocal, bit 3=GlobalPos, bit 4=GlobalRot, bit 5=GlobalScale
    private int _transformDirty = 0;
    private float4x4 _cachedTRS = float4x4.Identity;
    private float4x4 _cachedLocalToWorld = float4x4.Identity;
    private float4x4 _cachedWorldToLocal = float4x4.Identity;
    private float3 _cachedGlobalPosition = float3.Zero;
    private floatQ _cachedGlobalRotation = floatQ.Identity;
    private float3 _cachedGlobalScale = float3.One;

    /// <summary>
    /// Unique reference ID for network synchronization.
    /// </summary>
    public ulong RefID { get; private set; }

    /// <summary>
    /// The World this Slot belongs to.
    /// </summary>
    public World World { get; private set; }

    /// <summary>
    /// The hook that implements this slot in the engine (Godot Node3D).
    /// </summary>
    public IHook<Slot> Hook { get; private set; }

    /// <summary>
    /// Explicit interface implementation for non-generic IHook.
    /// </summary>
    IHook IImplementable.Hook => Hook;

    /// <summary>
    /// Explicit interface implementation for IImplementable.Slot (Slot refers to itself).
    /// </summary>
    Slot IImplementable.Slot => this;

    /// <summary>
    /// Whether this Slot has been destroyed.
    /// </summary>
    public bool IsDestroyed => _isDestroyed;

    /// <summary>
    /// Whether this Slot has been initialized.
    /// </summary>
    public bool IsInitialized { get; private set; }

    /// <summary>
    /// The parent Slot in the hierarchy (null if root).
    /// </summary>
    public Slot Parent
    {
        get => _parent;
        set
        {
            if (_parent == value) return;

            _parent?.RemoveChild(this);
            _parent = value;
            _parent?.AddChild(this);

            // Invalidate global transforms when parent changes
            InvalidateGlobalTransforms();
        }
    }

    /// <summary>
    /// Read-only list of child Slots.
    /// </summary>
    public IReadOnlyList<Slot> Children => _children.AsReadOnly();

    /// <summary>
    /// Number of child Slots.
    /// </summary>
    public int ChildCount => _children.Count;

    /// <summary>
    /// Read-only list of Components attached to this Slot.
    /// </summary>
    public IReadOnlyList<Component> Components => _components.AsReadOnly();

    /// <summary>
    /// Number of Components attached to this Slot.
    /// </summary>
    public int ComponentCount => _components.Count;

    /// <summary>
    /// Name of this Slot (synchronized across network).
    /// </summary>
    public Sync<string> SlotName { get; private set; }

    /// <summary>
    /// Whether this Slot is active in the hierarchy.
    /// </summary>
    public Sync<bool> ActiveSelf { get; private set; }

    /// <summary>
    /// Position in local space (synchronized).
    /// </summary>
    public Sync<float3> LocalPosition { get; private set; }

    /// <summary>
    /// Rotation in local space (synchronized).
    /// </summary>
    public Sync<floatQ> LocalRotation { get; private set; }

    /// <summary>
    /// Scale in local space (synchronized).
    /// </summary>
    public Sync<float3> LocalScale { get; private set; }

    /// <summary>
    /// Tag for categorization and searching.
    /// </summary>
    public Sync<string> Tag { get; private set; }

    // Transform cache constants
    private const int DIRTY_TRS = 1;
    private const int DIRTY_LOCAL_TO_WORLD = 2;
    private const int DIRTY_WORLD_TO_LOCAL = 4;
    private const int DIRTY_GLOBAL_POSITION = 8;
    private const int DIRTY_GLOBAL_ROTATION = 16;
    private const int DIRTY_GLOBAL_SCALE = 32;
    private const int DIRTY_ALL_GLOBAL = DIRTY_LOCAL_TO_WORLD | DIRTY_WORLD_TO_LOCAL |
                                          DIRTY_GLOBAL_POSITION | DIRTY_GLOBAL_ROTATION | DIRTY_GLOBAL_SCALE;

    /// <summary>
    /// Whether this Slot and its contents should persist when saved.
    /// </summary>
    public Sync<bool> Persistent { get; private set; }

    /// <summary>
    /// Invalidates the transform cache and propagates to all children.
    /// </summary>
    private void InvalidateTransformCache()
    {
        // Mark TRS as dirty
        _transformDirty |= DIRTY_TRS;

        // Mark all global transforms as dirty
        InvalidateGlobalTransforms();
    }

    /// <summary>
    /// Invalidates global transform caches (called when local transform or parent changes).
    /// </summary>
    private void InvalidateGlobalTransforms()
    {
        // If already dirty, no need to propagate
        if ((_transformDirty & DIRTY_ALL_GLOBAL) == DIRTY_ALL_GLOBAL)
            return;

        // Mark all global transforms as dirty
        _transformDirty |= DIRTY_ALL_GLOBAL;

        // Propagate to all children
        foreach (var child in _children)
        {
            child.InvalidateGlobalTransforms();
        }
    }

    /// <summary>
    /// Ensures the TRS matrix is valid.
    /// </summary>
    private void EnsureValidTRS()
    {
        if ((_transformDirty & DIRTY_TRS) != 0)
        {
            _cachedTRS = float4x4.TRS(LocalPosition.Value, LocalRotation.Value, LocalScale.Value);
            _transformDirty &= ~DIRTY_TRS;
        }
    }

    /// <summary>
    /// Ensures the LocalToWorld matrix is valid.
    /// </summary>
    private void EnsureValidLocalToWorld()
    {
        if ((_transformDirty & DIRTY_LOCAL_TO_WORLD) != 0)
        {
            if (_parent == null)
            {
                EnsureValidTRS();
                _cachedLocalToWorld = _cachedTRS;
            }
            else
            {
                _parent.EnsureValidLocalToWorld();
                EnsureValidTRS();
                _cachedLocalToWorld = _parent._cachedLocalToWorld * _cachedTRS;
            }
            _transformDirty &= ~DIRTY_LOCAL_TO_WORLD;
        }
    }

    /// <summary>
    /// Ensures the WorldToLocal matrix is valid.
    /// </summary>
    private void EnsureValidWorldToLocal()
    {
        if ((_transformDirty & DIRTY_WORLD_TO_LOCAL) != 0)
        {
            EnsureValidLocalToWorld();
            _cachedWorldToLocal = _cachedLocalToWorld.Inverse;
            _transformDirty &= ~DIRTY_WORLD_TO_LOCAL;
        }
    }

    /// <summary>
    /// Position in global/world space.
    /// </summary>
    public float3 GlobalPosition
    {
        get
        {
            if ((_transformDirty & DIRTY_GLOBAL_POSITION) != 0)
            {
                EnsureValidLocalToWorld();
                // Extract position from the last column of the matrix
                _cachedGlobalPosition = new float3(_cachedLocalToWorld.c3.x, _cachedLocalToWorld.c3.y, _cachedLocalToWorld.c3.z);
                _transformDirty &= ~DIRTY_GLOBAL_POSITION;
            }
            return _cachedGlobalPosition;
        }
        set
        {
            if (_parent == null)
            {
                LocalPosition.Value = value;
                // Cache the value since we know it's already in global space
                _cachedGlobalPosition = value;
                _transformDirty &= ~DIRTY_GLOBAL_POSITION;
                return;
            }

            var parentScale = _parent.GlobalScale;
            var invParentRot = _parent.GlobalRotation.Inverse;
            var delta = value - _parent.GlobalPosition;
            var unrotated = invParentRot * delta;

            // Avoid division by zero by clamping scale
            float3 safeScale = new float3(
                parentScale.x == 0 ? 1 : parentScale.x,
                parentScale.y == 0 ? 1 : parentScale.y,
                parentScale.z == 0 ? 1 : parentScale.z
            );

            LocalPosition.Value = new float3(
                unrotated.x / safeScale.x,
                unrotated.y / safeScale.y,
                unrotated.z / safeScale.z
            );

            // Cache the global position since we just set it
            if ((_parent._transformDirty & DIRTY_ALL_GLOBAL) == 0)
            {
                _cachedGlobalPosition = value;
                _transformDirty &= ~DIRTY_GLOBAL_POSITION;
            }
        }
    }

    /// <summary>
    /// Rotation in global/world space.
    /// </summary>
    public floatQ GlobalRotation
    {
        get
        {
            if ((_transformDirty & DIRTY_GLOBAL_ROTATION) != 0)
            {
                if (_parent == null)
                {
                    _cachedGlobalRotation = LocalRotation.Value;
                }
                else
                {
                    _cachedGlobalRotation = _parent.GlobalRotation * LocalRotation.Value;
                }
                _transformDirty &= ~DIRTY_GLOBAL_ROTATION;
            }
            return _cachedGlobalRotation;
        }
        set
        {
            if (_parent == null)
            {
                LocalRotation.Value = value;
                // Cache the value since we know it's already in global space
                _cachedGlobalRotation = value;
                _transformDirty &= ~DIRTY_GLOBAL_ROTATION;
                return;
            }
            var invParentRot = _parent.GlobalRotation.Inverse;
            LocalRotation.Value = invParentRot * value;

            // Cache the global rotation since we just set it
            if ((_parent._transformDirty & DIRTY_GLOBAL_ROTATION) == 0)
            {
                _cachedGlobalRotation = value;
                _transformDirty &= ~DIRTY_GLOBAL_ROTATION;
            }
        }
    }

    /// <summary>
    /// Scale in local space.
    /// Convenience accessor for LocalScale.Value.
    /// </summary>
    public float3 Scale
    {
        get => LocalScale.Value;
        set => LocalScale.Value = value;
    }

    /// <summary>
    /// Scale in global/world space (component-wise multiplied up the hierarchy).
    /// </summary>
    public float3 GlobalScale
    {
        get
        {
            if ((_transformDirty & DIRTY_GLOBAL_SCALE) != 0)
            {
                if (_parent == null)
                {
                    _cachedGlobalScale = LocalScale.Value;
                }
                else
                {
                    var parentScale = _parent.GlobalScale;
                    var local = LocalScale.Value;
                    _cachedGlobalScale = new float3(parentScale.x * local.x, parentScale.y * local.y, parentScale.z * local.z);
                }
                _transformDirty &= ~DIRTY_GLOBAL_SCALE;
            }
            return _cachedGlobalScale;
        }
    }

    /// <summary>
    /// Full local TRS matrix.
    /// </summary>
    public float4x4 LocalTransform
    {
        get
        {
            EnsureValidTRS();
            return _cachedTRS;
        }
    }

    /// <summary>
    /// Full global TRS matrix (parent * local).
    /// Alias for LocalToWorld for consistency.
    /// </summary>
    public float4x4 GlobalTransform
    {
        get
        {
            EnsureValidLocalToWorld();
            return _cachedLocalToWorld;
        }
    }

    /// <summary>
    /// Transformation matrix from local space to world space.
    /// </summary>
    public float4x4 LocalToWorld
    {
        get
        {
            EnsureValidLocalToWorld();
            return _cachedLocalToWorld;
        }
    }

    /// <summary>
    /// Transformation matrix from world space to local space.
    /// </summary>
    public float4x4 WorldToLocal
    {
        get
        {
            EnsureValidWorldToLocal();
            return _cachedWorldToLocal;
        }
    }

    /// <summary>
    /// Transform a point from global (world) space to this slot's local space.
    /// </summary>
    public float3 GlobalPointToLocal(float3 globalPoint)
    {
        EnsureValidWorldToLocal();
        return _cachedWorldToLocal.MultiplyPoint(globalPoint);
    }

    /// <summary>
    /// Transform a point from global (world) space to this slot's local space (ref version).
    /// </summary>
    public float3 GlobalPointToLocal(in float3 globalPoint)
    {
        EnsureValidWorldToLocal();
        return _cachedWorldToLocal.MultiplyPoint(in globalPoint);
    }

    /// <summary>
    /// Transform a point from this slot's local space to global (world) space.
    /// </summary>
    public float3 LocalPointToGlobal(float3 localPoint)
    {
        EnsureValidLocalToWorld();
        return _cachedLocalToWorld.MultiplyPoint(localPoint);
    }

    /// <summary>
    /// Transform a point from this slot's local space to global (world) space (ref version).
    /// </summary>
    public float3 LocalPointToGlobal(in float3 localPoint)
    {
        EnsureValidLocalToWorld();
        return _cachedLocalToWorld.MultiplyPoint(in localPoint);
    }

    /// <summary>
    /// Transform a direction from global (world) space to this slot's local space.
    /// </summary>
    public float3 GlobalDirectionToLocal(float3 globalDirection)
    {
        EnsureValidWorldToLocal();
        return _cachedWorldToLocal.MultiplyVector(globalDirection);
    }

    /// <summary>
    /// Transform a direction from this slot's local space to global (world) space.
    /// </summary>
    public float3 LocalDirectionToGlobal(float3 localDirection)
    {
        EnsureValidLocalToWorld();
        return _cachedLocalToWorld.MultiplyVector(localDirection);
    }

    /// <summary>
    /// Transform a rotation from global (world) space to this slot's local space.
    /// </summary>
    public floatQ GlobalRotationToLocal(floatQ globalRotation)
    {
        return GlobalRotation.Inverse * globalRotation;
    }

    /// <summary>
    /// Transform a rotation from this slot's local space to global (world) space.
    /// </summary>
    public floatQ LocalRotationToGlobal(floatQ localRotation)
    {
        return GlobalRotation * localRotation;
    }

    /// <summary>
    /// Position in local space.
    /// Convenience accessor for LocalPosition.Value.
    /// </summary>
    public float3 Position
    {
        get => LocalPosition.Value;
        set => LocalPosition.Value = value;
    }

    /// <summary>
    /// Rotation in local space.
    /// Convenience accessor for LocalRotation.Value.
    /// </summary>
    public floatQ Quaternion
    {
        get => LocalRotation.Value;
        set => LocalRotation.Value = value;
    }

    /// <summary>
    /// Whether this Slot is active (considering parent chain).
    /// </summary>
    public bool IsActive
    {
        get
        {
            if (!ActiveSelf.Value)
                return false;

            if (_parent != null)
                return _parent.IsActive;

            return true;
        }
    }

    /// <summary>
    /// Whether this is the root slot (has no parent).
    /// </summary>
    public bool IsRootSlot => _parent == null;

    public Slot()
    {
        RefID = 0; // Will be assigned by World.RefIDAllocator during Initialize
        InitializeSyncFields();
    }

    private void InitializeSyncFields()
    {
        SlotName = new Sync<string>(this, "Slot");
        ActiveSelf = new Sync<bool>(this, true);
        LocalPosition = new Sync<float3>(this, float3.Zero);
        LocalRotation = new Sync<floatQ>(this, floatQ.Identity);
        LocalScale = new Sync<float3>(this, float3.One);
        Tag = new Sync<string>(this, string.Empty);
        Persistent = new Sync<bool>(this, true);

        // Hook up transform synchronization and cache invalidation
        LocalPosition.OnChanged += (val) =>
        {
            _position = val;
            InvalidateTransformCache();
        };

        LocalRotation.OnChanged += (val) =>
        {
            _rotation = val;
            InvalidateTransformCache();
        };

        LocalScale.OnChanged += (val) =>
        {
            _scale = val;
            InvalidateTransformCache();
        };

        // Hook up visibility synchronization
        ActiveSelf.OnChanged += (val) =>
        {
            _visible = val;
            Console.WriteLine($"[Slot '{SlotName.Value}'] ActiveSelf changed to {val}");
        };

        // Hook up name synchronization
        SlotName.OnChanged += (val) => _name = val ?? "Slot";
    }

    /// <summary>
    /// Initialize this Slot with a World context.
    /// </summary>
    public void Initialize(World world)
    {
        World = world;

        // Allocate RefID from current allocation context
        // (may be local or user context depending on caller)
        RefID = World?.RefIDAllocator?.AllocateID() ?? 0;
        IsInitialized = true;

        // Root slots have all transforms valid initially
        if (IsRootSlot)
        {
            _transformDirty = 0; // All caches are valid for root
            _cachedTRS = float4x4.TRS(LocalPosition.Value, LocalRotation.Value, LocalScale.Value);
            _cachedLocalToWorld = _cachedTRS;
            _cachedWorldToLocal = _cachedTRS.Inverse;
            _cachedGlobalPosition = LocalPosition.Value;
            _cachedGlobalRotation = LocalRotation.Value;
            _cachedGlobalScale = LocalScale.Value;
        }

        InitializeHook();

        foreach (var component in _components)
        {
            component.OnAwake();
        }
    }

    /// <summary>
    /// Create and assign the hook for this slot.
    /// </summary>
    private void InitializeHook()
    {
        if (World == null)
            return;

        Type hookType = World.HookTypes.GetHookType(typeof(Slot));
        if (hookType == null)
            return;

        Hook = (IHook<Slot>)Activator.CreateInstance(hookType);
        Hook?.AssignOwner(this);
        Hook?.Initialize();
    }

    /// <summary>
    /// Add a Component to this Slot.
    /// </summary>
    public T AttachComponent<T>() where T : Component, new()
    {
        // If not initialized yet, just create without allocation context
        if (!IsInitialized || World == null)
        {
            var uninitializedComponent = new T();
            uninitializedComponent.Initialize(this);
            _components.Add(uninitializedComponent);
            return uninitializedComponent;
        }

        // Peek/Block pattern for deterministic ID allocation:
        // 1. Check if parent slot is local and start local block if needed
        bool isLocal = RefIDAllocator.IsLocalID(RefID);
        if (isLocal)
        {
            World.RefIDAllocator.LocalAllocationBlockBegin();
        }

        // 2. Peek at next ID
        ulong nextID = World.RefIDAllocator.PeekID();

        // 3. Create component
        var component = new T();

        // 4. Begin allocation block at peeked position
        byte userByte = RefIDAllocator.GetUserByteFromRefID(nextID);
        ulong position = RefIDAllocator.GetPositionFromRefID(nextID);
        World.RefIDAllocator.AllocationBlockBegin(userByte, position);

        // 5. Initialize (allocates the peeked ID)
        component.Initialize(this);
        _components.Add(component);

        // 6. Call lifecycle methods
        component.OnAwake();
        component.OnInit();
        component.OnStart();

        // 7. End allocation block
        World.RefIDAllocator.AllocationBlockEnd();

        // 8. End local block if we started one
        if (isLocal)
        {
            World.RefIDAllocator.LocalAllocationBlockEnd();
        }

        return component;
    }

    /// <summary>
    /// Get the first Component of the specified type.
    /// </summary>
    public T GetComponent<T>() where T : Component
    {
        return _components.OfType<T>().FirstOrDefault();
    }

    /// <summary>
    /// Get all Components of the specified type.
    /// </summary>
    public IEnumerable<T> GetComponents<T>() where T : Component
    {
        return _components.OfType<T>();
    }

    /// <summary>
    /// Remove a Component from this Slot.
    /// </summary>
    public void RemoveComponent(Component component)
    {
        if (_components.Remove(component))
        {
            component.OnDestroy();
        }
    }

    /// <summary>
    /// Create a new child Slot.
    /// </summary>
    public Slot AddSlot(string name = "Slot")
    {
        // If this slot is not initialized, just create without allocation
        if (World == null)
        {
            var uninitializedSlot = new Slot();
            uninitializedSlot.SlotName.Value = name;
            uninitializedSlot._name = name;
            uninitializedSlot.Parent = this;
            return uninitializedSlot;
        }

        // Peek/Block pattern for deterministic ID allocation:
        // 1. Check if parent is local and start local block if needed
        bool isLocal = RefIDAllocator.IsLocalID(RefID);
        if (isLocal)
        {
            World.RefIDAllocator.LocalAllocationBlockBegin();
        }

        // 2. Peek at next ID
        ulong nextID = World.RefIDAllocator.PeekID();

        // 3. Create slot
        var slot = new Slot();
        slot.SlotName.Value = name;
        slot._name = name;

        // 4. Begin allocation block at peeked position
        byte userByte = RefIDAllocator.GetUserByteFromRefID(nextID);
        ulong position = RefIDAllocator.GetPositionFromRefID(nextID);
        World.RefIDAllocator.AllocationBlockBegin(userByte, position);

        // 5. Set parent BEFORE initialization (so IsRootSlot returns correct value)
        slot.Parent = this;

        // 6. Initialize (allocates the peeked ID)
        slot.Initialize(World);

        // 7. End allocation block
        World.RefIDAllocator.AllocationBlockEnd();

        // 8. End local block if we started one
        if (isLocal)
        {
            World.RefIDAllocator.LocalAllocationBlockEnd();
        }

        return slot;
    }

    /// <summary>
    /// Add an existing Slot as a child.
    /// </summary>
    public void AddChild(Slot child)
    {
        if (child == null || _children.Contains(child)) return;

        _children.Add(child);
    }

    /// <summary>
    /// Remove a child Slot.
    /// </summary>
    public void RemoveChild(Slot child)
    {
        _children.Remove(child);
    }

    /// <summary>
    /// Find the first child Slot with the specified name.
    /// </summary>
    public Slot FindChild(string name, bool recursive = false)
    {
        foreach (var child in _children)
        {
            if (child.SlotName.Value == name)
                return child;

            if (recursive)
            {
                var found = child.FindChild(name, true);
                if (found != null) return found;
            }
        }
        return null;
    }

    /// <summary>
    /// Find all child Slots with the specified tag.
    /// </summary>
    public IEnumerable<Slot> FindChildrenByTag(string tag, bool recursive = false)
    {
        foreach (var child in _children)
        {
            if (child.Tag.Value == tag)
                yield return child;

            if (recursive)
            {
                foreach (var found in child.FindChildrenByTag(tag, true))
                    yield return found;
            }
        }
    }

    /// <summary>
    /// Get the hierarchical path of this slot (e.g. "Root/Parent/Child").
    /// </summary>
    public string GetPath()
    {
        if (_parent == null)
            return SlotName.Value;

        return _parent.GetPath() + "/" + SlotName.Value;
    }

    /// <summary>
    /// Destroy this Slot and all its children and components.
    /// </summary>
    public void Destroy()
    {
        if (_isDestroyed) return;

        _isDestroyed = true;

        // Destroy all children
        foreach (var child in _children.ToArray())
        {
            child.Destroy();
        }

        // Destroy all components
        foreach (var component in _components.ToArray())
        {
            component.OnDestroy();
        }

        _children.Clear();
        _components.Clear();

        // Remove from parent
        _parent?.RemoveChild(this);

        // Remove from World
        World?.UnregisterSlot(this);
    }

    /// <summary>
    /// Update all components.
    /// </summary>
    public void UpdateComponents(float delta)
    {
        // ALWAYS update components, even if slot is inactive
        // This allows components to handle visibility changes (e.g., Dashboard toggling)
        // Components should check Slot.ActiveSelf themselves if needed
        foreach (var component in _components)
        {
            if (component.Enabled.Value)
            {
                component.OnUpdate(delta);
            }
        }
    }
}
