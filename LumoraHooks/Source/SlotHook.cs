// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Godot;
using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Logging;

namespace Lumora.Godot.Hooks;

// Lazy creation: Node3D is only created when RequestNode3D() is first called. Reference counting
// tracks how many components need the Node3D; when count reaches 0 and shouldDestroy is set, it's freed.
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
		GeneratedNode3D.Name = SafeNodeName(Owner.SlotName.Value);

		_nodeToSlot[GeneratedNode3D] = Owner;

		UpdateParent();
		SetData();
	}

	// Godot forbids empty node names (some engines allow them). During load a child slot is created
	// before its name member decodes, so fall back to a non-empty default instead of erroring.
	private static string SafeNodeName(string? name)
		=> string.IsNullOrWhiteSpace(name) ? "Slot" : name;

	private void UpdateParent()
	{
		if (ShouldDeferHierarchy)
		{
			return;
		}

		// Defer if parent is unknown (ParentSlotRef not decoded yet) or pending (waiting for async resolution)
		// This prevents orphaned slots from being incorrectly attached to world root
		// during network decode when sync members haven't been decoded yet
		if (Owner.IsParentUnknown)
		{
			Lumora.Core.Logging.Logger.Log($"SlotHook: Deferring hierarchy for '{Owner.SlotName.Value}' - parent ref not decoded yet");
			return;
		}
		if (Owner.HasPendingParent)
		{
			Lumora.Core.Logging.Logger.Log($"SlotHook: Deferring hierarchy for '{Owner.SlotName.Value}' - parent pending resolution");
			return;
		}

		if (_lastParent == Owner.Parent && !Owner.IsRootSlot)
		{
			return;
		}

		_lastParent = Owner.Parent;

		if (_parentHook != null)
		{
			_parentHook.FreeNode3D();
			_parentHook = null!;
		}

		if (_lastParent != null && !Owner.IsRootSlot)
		{
			_parentHook = (SlotHook)_lastParent.Hook;
			if (_parentHook != null)
			{
				Node3D parentNode = _parentHook.RequestNode3D();
				if (GeneratedNode3D.GetParent() != parentNode)
				{
					if (GeneratedNode3D.GetParent() != null)
					{
						GeneratedNode3D.Reparent(parentNode, false);
					}
					else
					{
						parentNode.AddChild(GeneratedNode3D);
					}
				}
				Lumora.Core.Logging.Logger.Log($"SlotHook: Attached child slot '{Owner.SlotName.Value}' to parent '{_lastParent.SlotName.Value}'");
			}
			else
			{
				Lumora.Core.Logging.Logger.Warn($"SlotHook: Parent hook is null for slot '{Owner.SlotName.Value}' (parent: '{_lastParent.SlotName.Value}')");
			}
		}
		else
		{
			// Root slot - attach to world root ONLY if this is the actual World.RootSlot
			// Don't use IsTrueRootSlot because ParentSlotRef.IsInInitPhase becomes false
			// during slot initialization, but the actual Value is decoded later as a separate
			// sync element. This causes false positives for "true root slot" detection.
			bool isActualRootSlot = Owner == Owner.World?.RootSlot;
			if (isActualRootSlot)
			{
				var worldRoot = Owner.World!.GodotSceneRoot as Node3D;
				if (worldRoot != null)
				{
					if (GeneratedNode3D.GetParent() != worldRoot)
					{
						if (GeneratedNode3D.GetParent() != null)
						{
							GeneratedNode3D.Reparent(worldRoot, false);
						}
						else
						{
							worldRoot.AddChild(GeneratedNode3D);
						}
					}
					Lumora.Core.Logging.Logger.Log($"SlotHook: Attached root slot '{Owner.SlotName.Value}' to world root");
				}
				else
				{
					// Not an error, just startup ordering: the root slot initializes before the world's own scene
					// node exists (World.Initialize builds the root slot; the world hook creates the scene root and
					// reparents this node right after). Fires on every world creation, so trace it, don't warn. - xlinka
					Lumora.Core.Logging.Logger.Log($"SlotHook: root slot '{Owner.SlotName.Value}' waiting for world scene root (the world hook attaches it once created)");
				}
			}
			else
			{
				// Not World.RootSlot and no parent - check if we should fall back to RootSlot
				// This happens when:
				// 1. ParentSlotRef.Value was decoded as RefID.Null (true orphan, should parent to RootSlot)
				// 2. We're still waiting for parent decode (defer)
				var parentRefValue = Owner.ParentSlotRef?.Value ?? RefID.Null;
				bool worldIsRunning = Owner.World?.State == World.WorldState.Running;
				bool parentRefIsNull = parentRefValue.IsNull;

				if (worldIsRunning && parentRefIsNull && Owner.World?.RootSlot != null)
				{
					var rootSlot = Owner.World.RootSlot;
					var rootHook = rootSlot.Hook as SlotHook;
					if (rootHook != null)
					{
						_parentHook = rootHook;
						Node3D parentNode = _parentHook.RequestNode3D();
						if (GeneratedNode3D.GetParent() != parentNode)
						{
							if (GeneratedNode3D.GetParent() != null)
							{
								GeneratedNode3D.Reparent(parentNode, false);
							}
							else
							{
								parentNode.AddChild(GeneratedNode3D);
							}
						}
						Lumora.Core.Logging.Logger.Log($"SlotHook: Attached orphan slot '{Owner.SlotName.Value}' to RootSlot '{rootSlot.SlotName.Value}' (fallback)");
					}
				}
				else
				{
					// Still waiting for parent decode or world not running yet
					Lumora.Core.Logging.Logger.Log($"SlotHook: Deferring attachment for '{Owner.SlotName.Value}' - waiting for parent decode (ParentRef.Value={parentRefValue}, WorldRunning={worldIsRunning})");
				}
			}
		}
	}

	private void SetData()
	{
		if (GeneratedNode3D == null) return;

		GeneratedNode3D.Visible = Owner.ActiveSelf.Value;
		GeneratedNode3D.Position = ToGodotVector3(Owner.LocalPosition.Value);
		GeneratedNode3D.Quaternion = ToGodotQuaternion(Owner.LocalRotation.Value);
		GeneratedNode3D.Scale = ToGodotVector3(Owner.LocalScale.Value);
	}

	private void UpdateData()
	{
		if (GeneratedNode3D == null) return;

		if (Owner.ActiveSelf.GetWasChangedAndClear())
		{
			GeneratedNode3D.Visible = Owner.ActiveSelf.Value;
		}

		if (Owner.LocalPosition.GetWasChangedAndClear())
		{
			var newPos = ToGodotVector3(Owner.LocalPosition.Value);
			GeneratedNode3D.Position = newPos;
		}

		if (Owner.LocalRotation.GetWasChangedAndClear())
		{
			GeneratedNode3D.Quaternion = ToGodotQuaternion(Owner.LocalRotation.Value);
		}

		if (Owner.LocalScale.GetWasChangedAndClear())
		{
			GeneratedNode3D.Scale = ToGodotVector3(Owner.LocalScale.Value);
		}

		var slotName = SafeNodeName(Owner.SlotName.Value);
		if (Owner.SlotName.GetWasChangedAndClear())
		{
			GeneratedNode3D.Name = slotName;
		}
		else if (GeneratedNode3D.Name != slotName)
		{
			GeneratedNode3D.Name = slotName;
		}
	}

	public override void Initialize()
	{
		if (ShouldDeferHierarchy)
		{
			if (!_didDeferLog)
			{
				Lumora.Core.Logging.Logger.Log($"SlotHook.Initialize: Deferring Node3D creation for '{Owner.SlotName.Value}'");
				_didDeferLog = true;
			}
			return;
		}

		GenerateNode3D();
		Lumora.Core.Logging.Logger.Log($"SlotHook.Initialize: Created Node3D for slot '{Owner.SlotName.Value}'");
	}

	public override void ApplyChanges()
	{
		if (GeneratedNode3D == null || !GodotObject.IsInstanceValid(GeneratedNode3D))
		{
			if (ShouldDeferHierarchy)
			{
				return;
			}

			GenerateNode3D();
		}

		if (GeneratedNode3D != null && GodotObject.IsInstanceValid(GeneratedNode3D))
		{
			Slot parent = Owner.Parent;
			if (parent != _lastParent)
			{
				UpdateParent();
			}

			UpdateData();
		}
	}

	public override void Destroy(bool destroyingWorld)
	{
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
}

public interface ISlotHook : IHook<Slot>
{
}

