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

	/// <summary>
	/// Whether this Slot and its contents should persist when saved.
	/// </summary>
	public Sync<bool> Persistent { get; private set; }

	/// <summary>
	/// Position in global/world space.
	/// </summary>
	public float3 GlobalPosition
	{
		get
		{
			return LocalPosition.Value;
		}
		set
		{
			LocalPosition.Value = value;
		}
	}

	/// <summary>
	/// Rotation in global/world space.
	/// </summary>
	public floatQ GlobalRotation
	{
		get
		{
			return LocalRotation.Value;
		}
		set
		{
			LocalRotation.Value = value;
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

		// Hook up transform synchronization
		LocalPosition.OnChanged += (val) => _position = val;
		LocalRotation.OnChanged += (val) => _rotation = val;
		LocalScale.OnChanged += (val) => _scale = val;

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
