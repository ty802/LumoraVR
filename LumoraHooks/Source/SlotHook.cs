// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Godot;
using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Logging;

namespace Lumora.Godot.Hooks;

// A slot's Node3D is made when the slot is, except for a slot inside a canvas or a local one, which gets
// one only when RequestNode3D() is first called. Reference counting tracks how many components need the
// Node3D; when count reaches 0 and shouldDestroy is set, it's freed.
[ImplementableHook(typeof(Slot))]
public class SlotHook : Hook<Slot>, ISlotHook
{
	// Static registry to find Slot from Godot Node
	private static readonly Dictionary<Node, Slot> _nodeToSlot = new();

	private Slot _lastParent = null!;
	private int _node3DRequests;
	private bool _shouldDestroy;
	private SlotHook _parentHook = null!;
	private WorldHook _worldHook = null!;
	private bool _didDeferLog;

	// LocalViewOverride pushes its per-view transform through here instead of writing the node itself.
	// The slot stays the only writer of its own transform, so an override cannot lose a race with the
	// slot's own update and flicker for a frame. Null falls through to the slot's real value. -xlinka
	private float3? _overridePosition;
	private floatQ? _overrideRotation;
	private float3? _overrideScale;

	// What this hook last pushed to the node. A flush lands here for every change event on the slot, and
	// the silent write paths (IK bind-pose reset, stream playback, dynamic bones) fire that event without
	// checking whether the value moved, so a resting avatar re-flushes every bone every frame with the
	// numbers it already has. Comparing against the last push turns those into a compare and a return
	// instead of three marshalled setters and three transform propagations through the node's subtree.
	// The name is the same story with an allocation on top: reading Node.Name marshals a StringName and
	// comparing it to a string marshals another, both finalizable, on every flush of every slot. -xlinka
	private Vector3 _appliedPosition;
	private Quaternion _appliedRotation;
	private Vector3 _appliedScale;
	private bool _appliedVisible;
	private string _appliedName = null!;

	private bool ShouldDeferHierarchy
	{
		get
		{
			if (Owner?.World == null)
				return false;

			// Defer if we're still decoding a batch - sync fields (Name, ParentSlotRef) may not be populated yet
			var refController = Owner.World.ReferenceController;
			if (refController?.IsDecodingBatch == true)
				return true;

			// Defer if world not running yet (client still connecting)
			if (!Owner.World.IsAuthority && Owner.World.State != World.WorldState.Running)
				return true;

			return false;
		}
	}

	public Node3D GeneratedNode3D { get; private set; } = null!;

	public WorldHook WorldHook => _worldHook ??= (WorldHook)Owner.World.Hook;

	public static IHook<Slot> Constructor()
	{
		return new SlotHook();
	}

	public static Slot? GetSlotFromNode(Node? node)
	{
		var current = node;
		while (current != null)
		{
			if (_nodeToSlot.TryGetValue(current, out var slot))
				return slot;
			current = current.GetParent();
		}
		return null;
	}

	public Node3D ForceGetNode3D()
	{
		if (GeneratedNode3D == null || !GodotObject.IsInstanceValid(GeneratedNode3D))
		{
			GenerateNode3D();
		}
		return GeneratedNode3D!;
	}

	public Node3D RequestNode3D()
	{
		_node3DRequests++;
		return ForceGetNode3D();
	}

	public void FreeNode3D()
	{
		_node3DRequests--;
		TryDestroy();
	}

	private void TryDestroy(bool destroyingWorld = false)
	{
		if (!_shouldDestroy || _node3DRequests > 0)
		{
			return;
		}

		if (!destroyingWorld)
		{
			if (GeneratedNode3D != null && GodotObject.IsInstanceValid(GeneratedNode3D))
			{
				_nodeToSlot.Remove(GeneratedNode3D);
				GeneratedNode3D.QueueFree();
			}

			_parentHook?.FreeNode3D();
		}
		else if (GeneratedNode3D != null)
		{
			// Still need to unregister even when destroying world
			_nodeToSlot.Remove(GeneratedNode3D);
		}

		GeneratedNode3D = null!;
		_lastParent = null!;
		_parentHook = null!;
	}

	private void GenerateNode3D()
	{
		GeneratedNode3D = new Node3D();
		_appliedName = SafeNodeName(Owner.SlotName.Value);
		GeneratedNode3D.Name = _appliedName;

		_nodeToSlot[GeneratedNode3D] = Owner;

		UpdateParent();
		SetData();
	}

	// Godot forbids empty node names. During load a child slot is created
	// before its name member decodes, so fall back to a non-empty default instead of erroring.
	private static string SafeNodeName(string? name)
		=> string.IsNullOrWhiteSpace(name) ? "Slot" : name;

	private bool _hierarchyRetryQueued;

	private void RetryHierarchy()
	{
		if (_hierarchyRetryQueued || _shouldDestroy || Owner.IsDestroyed)
			return;
		_hierarchyRetryQueued = true;
		// Never enqueue directly from ApplyChanges: the slot queue drains until empty.
		Owner.World.RunInUpdates(1, () =>
		{
			_hierarchyRetryQueued = false;
			if (!_shouldDestroy && !Owner.IsDestroyed)
				Owner.World.UpdateManager?.RegisterHookUpdate(Owner);
		});
	}

	private void UpdateParent()
	{
		if (ShouldDeferHierarchy || Owner.IsParentUnknown || Owner.HasPendingParent)
		{
			RetryHierarchy();
			return;
		}

		var parent = Owner.Parent;
		if (_lastParent == parent && GeneratedNode3D.GetParent() != null && !Owner.IsRootSlot)
			return;

		SlotHook? parentHook = null;
		Node3D? parentNode;
		if (Owner == Owner.World.RootSlot)
		{
			parentNode = Owner.World.GodotSceneRoot as Node3D;
		}
		else
		{
			var parentSlot = parent;
			if (parentSlot == null && Owner.ParentSlotRef.Value.IsNull
				&& Owner.World.State == World.WorldState.Running)
				parentSlot = Owner.World.RootSlot;
			parentHook = parentSlot?.Hook as SlotHook;
			if (parentHook == null)
			{
				RetryHierarchy();
				return;
			}
			parentNode = parentHook.ForceGetNode3D();
		}

		if (parentNode == null || !GodotObject.IsInstanceValid(parentNode) || parentNode.IsQueuedForDeletion())
		{
			RetryHierarchy();
			return;
		}

		// Keep the existing attachment until the replacement is usable, and cache the parent only
		// after success. A missing parent hook used to be cached forever, until grabbing reparented it.
		if (parentHook != _parentHook)
			parentHook?.RequestNode3D();
		if (GeneratedNode3D.GetParent() != parentNode)
		{
			if (GeneratedNode3D.GetParent() != null)
				GeneratedNode3D.Reparent(parentNode, false);
			else
				parentNode.AddChild(GeneratedNode3D);
		}
		if (parentHook != _parentHook)
			_parentHook?.FreeNode3D();
		_parentHook = parentHook!;
		_lastParent = parent;
	}
	// Set or clear the render-only transform override. Pass null for a component to leave that part of
	// the transform alone. Callers must be on the main thread; this writes the node immediately so an
	// override that changes without the slot changing still lands.
	public void SetLocalTransformOverride(float3? position, floatQ? rotation, float3? scale)
	{
		if (SameOverride(_overridePosition, position)
			&& SameOverride(_overrideRotation, rotation)
			&& SameOverride(_overrideScale, scale))
			return;

		_overridePosition = position;
		_overrideRotation = rotation;
		_overrideScale = scale;

		if (GeneratedNode3D != null && GodotObject.IsInstanceValid(GeneratedNode3D))
		{
			SetData();
			MovingShadowCasters.NoteMoved(this);
		}
	}

	private void SetData()
	{
		if (GeneratedNode3D == null) return;

		_appliedVisible = Owner.ActiveSelf.Value && !Owner.IsLocallyHidden;
		GeneratedNode3D.Visible = _appliedVisible;
		_appliedPosition = ToGodotVector3(_overridePosition ?? Owner.LocalPosition.Value);
		GeneratedNode3D.Position = _appliedPosition;
		_appliedRotation = ToGodotQuaternion(_overrideRotation ?? Owner.LocalRotation.Value);
		GeneratedNode3D.Quaternion = _appliedRotation;
		_appliedScale = ToGodotVector3(_overrideScale ?? Owner.LocalScale.Value);
		GeneratedNode3D.Scale = _appliedScale;
	}

	private void UpdateData()
	{
		if (GeneratedNode3D == null) return;

		// Slot.SetLocallyHidden moves no field, so the answer is compared rather than waited for.
		bool activeChanged = Owner.ActiveSelf.GetWasChangedAndClear();
		bool visible = Owner.ActiveSelf.Value && !Owner.IsLocallyHidden;
		if (activeChanged || visible != _appliedVisible)
		{
			_appliedVisible = visible;
			GeneratedNode3D.Visible = visible;
		}

		bool moved = false;
		// A simulated body draws its node itself (DrivePose); the slot's pose would only drag it back to the
		// last physics step.
		bool driven = Owner.NodePoseDriven;
		if (Owner.LocalPosition.GetWasChangedAndClear() && !driven)
		{
			var position = ToGodotVector3(_overridePosition ?? Owner.LocalPosition.Value);
			if (position != _appliedPosition)
			{
				_appliedPosition = position;
				GeneratedNode3D.Position = position;
				moved = true;
			}
		}

		if (Owner.LocalRotation.GetWasChangedAndClear() && !driven)
		{
			var rotation = ToGodotQuaternion(_overrideRotation ?? Owner.LocalRotation.Value);
			if (rotation != _appliedRotation)
			{
				_appliedRotation = rotation;
				GeneratedNode3D.Quaternion = rotation;
				moved = true;
			}
		}

		if (Owner.LocalScale.GetWasChangedAndClear())
		{
			var scale = ToGodotVector3(_overrideScale ?? Owner.LocalScale.Value);
			if (scale != _appliedScale)
			{
				_appliedScale = scale;
				GeneratedNode3D.Scale = scale;
				moved = true;
			}
		}

		// A slot that keeps moving hands the shadows under it to copies that follow at a lower rate, so the
		// lights around it are not re-rendered every frame (see MovingShadowCasters).
		if (moved)
			MovingShadowCasters.NoteMoved(this);

		// Compared against what WE asked for, not what the node reports: siblings that share a name get
		// uniquified by the engine on attach, and asking the node back and re-setting it on every flush
		// only made it uniquify the same name again, forever. -xlinka
		//
		// Never on the changed flag alone. A copy's name is written as it is made, so the first flush saw the
		// flag up and named the node again, and naming a node beside siblings of the same name has the engine
		// try Name2, Name3 and so on until one is free: ten thousand copied cubes under one holder took six
		// seconds of that in a single frame. A node that already carries the name it was made with keeps it.
		Owner.SlotName.GetWasChangedAndClear();
		var slotName = SafeNodeName(Owner.SlotName.Value);
		if (!string.Equals(_appliedName, slotName, System.StringComparison.Ordinal))
		{
			_appliedName = slotName;
			GeneratedNode3D.Name = slotName;
		}
	}

	// A slot inside a canvas is laid out and drawn by the canvas: its rect, its words and its pictures all go
	// into the canvas's chunk meshes, which sit on slots of their own. A local slot is this machine's own
	// helper, a canvas chunk, a cache of material clones, a mesh a view draws. Either gets a node only when
	// something on it asks for one, a mesh or a collider, and that asks for its parents' too. Every slot got
	// one, so a thousand open graph cards carried thirty thousand empty nodes through every Godot frame.
	// -xlinka
	private bool WaitsForRequest
		=> _node3DRequests == 0 && (Owner.IsLocalElement || Owner.Parent?.GetComponent<Helio.UI.RectTransform>() != null);

	public override void Initialize()
	{
		if (WaitsForRequest)
			return;

		if (ShouldDeferHierarchy)
		{
			if (!_didDeferLog)
			{
				Lumora.Core.Logging.Logger.Debug($"SlotHook.Initialize: Deferring Node3D creation for '{Owner.SlotName.Value}'");
				_didDeferLog = true;
			}
			return;
		}

		GenerateNode3D();
		Lumora.Core.Logging.Logger.Debug($"SlotHook.Initialize: Created Node3D for slot '{Owner.SlotName.Value}'");
	}

	public override void ApplyChanges()
	{
		if (GeneratedNode3D == null || !GodotObject.IsInstanceValid(GeneratedNode3D))
		{
			if (ShouldDeferHierarchy || WaitsForRequest)
			{
				return;
			}

			GenerateNode3D();
			if (GeneratedNode3D == null || !GodotObject.IsInstanceValid(GeneratedNode3D))
			{
				return;
			}
		}

		if (Owner.Parent != _lastParent || GeneratedNode3D.GetParent() == null)
		{
			UpdateParent();
		}

		UpdateData();
	}

	// Where a simulated body is drawn, between physics steps as well as on them, given in the parent node's
	// space. One marshalled write for both, and none while it has not moved.
	public void DrivePose(in Vector3 position, in Quaternion rotation)
	{
		var node = GeneratedNode3D;
		if (node == null || (position == _appliedPosition && rotation == _appliedRotation))
			return;
		_appliedPosition = position;
		_appliedRotation = rotation;
		var basis = new Basis(rotation);
		basis.Column0 *= _appliedScale.X;
		basis.Column1 *= _appliedScale.Y;
		basis.Column2 *= _appliedScale.Z;
		node.Transform = new Transform3D(basis, position);
	}

	// The node was left where it was while its renderers were drawn elsewhere: whatever comes next writes its
	// pose, even one equal to the last this hook wrote.
	public void ForgetDrivenPose()
	{
		_appliedPosition = new Vector3(float.NaN, float.NaN, float.NaN);
		_appliedRotation = new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN);
	}

	// The drive is over: the node takes the slot's own pose again.
	public void EndDrive()
	{
		if (GeneratedNode3D == null || !GodotObject.IsInstanceValid(GeneratedNode3D))
			return;
		var position = ToGodotVector3(_overridePosition ?? Owner.LocalPosition.Value);
		var rotation = ToGodotQuaternion(_overrideRotation ?? Owner.LocalRotation.Value);
		if (position != _appliedPosition)
		{
			_appliedPosition = position;
			GeneratedNode3D.Position = position;
		}
		if (rotation != _appliedRotation)
		{
			_appliedRotation = rotation;
			GeneratedNode3D.Quaternion = rotation;
		}
	}

	public override void Destroy(bool destroyingWorld)
	{
		if (!destroyingWorld)
			MovingShadowCasters.Forget(this);
		_shouldDestroy = true;
		TryDestroy(destroyingWorld);
	}

	private static Vector3 ToGodotVector3(float3 v)
	{
		return new Vector3(v.x, v.y, v.z);
	}

	private static Quaternion ToGodotQuaternion(floatQ q)
	{
		return new Quaternion(q.x, q.y, q.z, q.w);
	}

	private static bool SameOverride(float3? a, float3? b)
	{
		if (a.HasValue != b.HasValue) return false;
		return !a.HasValue || a.Value == b!.Value;
	}

	private static bool SameOverride(floatQ? a, floatQ? b)
	{
		if (a.HasValue != b.HasValue) return false;
		return !a.HasValue || a.Value == b!.Value;
	}
}

public interface ISlotHook : IHook<Slot>
{
}
