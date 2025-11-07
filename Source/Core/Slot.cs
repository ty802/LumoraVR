using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Aquamarine.Source.Core;

/// <summary>
/// A Slot is a container for Components and other Slots (children).
/// Slots form a hierarchy and manage transforms in 3D space.
/// 
/// </summary>
public partial class Slot : Node3D, IWorldElement
{
	private static ulong _nextRefID = 1;
	private readonly List<Slot> _children = new();
	private readonly List<Component> _components = new();
	private Slot _parent;
	private bool _isDestroyed;

	/// <summary>
	/// Unique reference ID for network synchronization.
	/// </summary>
	public ulong RefID { get; private set; }

	/// <summary>
	/// The World this Slot belongs to.
	/// </summary>
	public World World { get; private set; }

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
	public new Slot Parent
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
	public Sync<Vector3> LocalPosition { get; private set; }

	/// <summary>
	/// Rotation in local space (synchronized).
	/// </summary>
	public Sync<Quaternion> LocalRotation { get; private set; }

	/// <summary>
	/// Scale in local space (synchronized).
	/// </summary>
	public Sync<Vector3> LocalScale { get; private set; }

	/// <summary>
	/// Tag for categorization and searching.
	/// </summary>
	public Sync<string> Tag { get; private set; }

	/// <summary>
	/// Whether this Slot and its contents should persist when saved.
	/// </summary>
	public Sync<bool> Persistent { get; private set; }

	public Slot()
	{
		RefID = _nextRefID++;
		InitializeSyncFields();
		
		// Set Godot node name to match SlotName for better debugging
		Name = "Slot";
	}

	private void InitializeSyncFields()
	{
		SlotName = new Sync<string>(this, "Slot");
		ActiveSelf = new Sync<bool>(this, true);
		LocalPosition = new Sync<Vector3>(this, Vector3.Zero);
		LocalRotation = new Sync<Quaternion>(this, Quaternion.Identity);
		LocalScale = new Sync<Vector3>(this, Vector3.One);
		Tag = new Sync<string>(this, string.Empty);
		Persistent = new Sync<bool>(this, true);

		// Hook up transform synchronization
		LocalPosition.OnChanged += (val) => Position = val;
		LocalRotation.OnChanged += (val) => Quaternion = val;
		LocalScale.OnChanged += (val) => Scale = val;
		
		// Hook up name synchronization - update Godot node name when SlotName changes
		SlotName.OnChanged += (val) => Name = val ?? "Slot";
	}

	/// <summary>
	/// Initialize this Slot with a World context.
	/// </summary>
	public void Initialize(World world)
	{
		World = world;
		IsInitialized = true;

		foreach (var component in _components)
		{
			component.OnAwake();
		}
	}

	/// <summary>
	/// Add a Component to this Slot.
	/// </summary>
	public T AttachComponent<T>() where T : Component, new()
	{
		var component = new T();
		component.Initialize(this);
		_components.Add(component);

		if (IsInitialized)
		{
			component.OnAwake();
			component.OnInit();
			component.OnStart();
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
		var slot = new Slot();
		slot.SlotName.Value = name;
		slot.Name = name; // Set Godot node name immediately
		
		// Initialize the slot if this slot is initialized
		if (World != null)
		{
			slot.Initialize(World);
		}
		
		slot.Parent = this;
		return slot;
	}

	/// <summary>
	/// Add an existing Slot as a child.
	/// </summary>
	public void AddChild(Slot child)
	{
		if (child == null || _children.Contains(child)) return;

		_children.Add(child);
		base.AddChild(child);
	}

	/// <summary>
	/// Remove a child Slot.
	/// </summary>
	public void RemoveChild(Slot child)
	{
		if (_children.Remove(child))
		{
			base.RemoveChild(child);
		}
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

		QueueFree();
	}

	public override void _Process(double delta)
	{
		if (!ActiveSelf.Value) return;

		// Update all components
		foreach (var component in _components)
		{
			if (component.Enabled.Value)
			{
				component.OnUpdate((float)delta);
			}
		}
	}
}
