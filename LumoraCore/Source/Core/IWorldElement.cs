// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking.Sync;
using Lumora.Warden;

namespace Lumora.Core;

public interface IWorldElement : IPermissionTarget
{
    World World { get; }

	RefID ReferenceID { get; }

	bool IsInitialized { get; }

    bool IsDestroyed { get; }

	bool IsPersistent { get; }

	string ParentHierarchyToString();

	// PERMISSION GATE VIEW
	// The gate lives below the engine and knows nothing about these types, so every fact it needs is
	// answered here, once, instead of the gate type-switching on engine classes. Keep these as pure
	// reads: the moment one of them starts deciding something, the policy has leaked back into the
	// thing it is supposed to be gating. The switch ORDER is load-bearing - Slot and Component are both
	// Workers, and a Worker inherits its parent's ownership while those two carry their own. -xlinka

	ulong IPermissionTarget.Id => ReferenceID.RawValue;

	string IPermissionTarget.HierarchyPath => ParentHierarchyToString();

	bool IPermissionTarget.IsInitializing
		=> this is SyncElement { IsInInitPhase: true } or SyncElement { IsLoading: true };

	PermissionTargetKind IPermissionTarget.PermissionKind => this switch
	{
		Slot _ => PermissionTargetKind.Slot,
		Component _ => PermissionTargetKind.Component,
		SyncElement _ => PermissionTargetKind.SyncElement,
		Worker _ => PermissionTargetKind.Worker,
		_ => PermissionTargetKind.Other
	};

	IPermissionTarget? IPermissionTarget.OwnershipParent => this switch
	{
		Slot _ => null,
		Component component => component.Slot,
		SyncElement syncElement => syncElement.Parent,
		Worker worker => worker.Parent,
		_ => null
	};

	IPermissionActor? IPermissionTarget.StructuralOwner => this switch
	{
		Slot slot => slot.ActiveUser,
		Component component => component.Slot?.ActiveUser,
		_ => null
	};

	// A user owns everything parented under their own UserRoot - avatar, body nodes, tools, nameplate. This is
	// the STRUCTURAL ownership signal, and the reliable one: a user's allocation byte reads as 0 on their own
	// client until the host-authored AllocationID syncs across, and the cached ActiveUserRoot lags a beat behind
	// the body being built - both of which otherwise (wrongly) deny a user the right to drive their own
	// head/hands/nameplate. The User -> UserRoot link (UserRootRef) is replicated and set on both ends. -xlinka
	bool IPermissionTarget.IsUnderActorRoot(IPermissionActor actor)
	{
		if (actor?.RootElement is not Slot rootSlot)
		{
			return false;
		}

		// A component is under the root when its SLOT is; a component with no slot yet is under nothing.
		var slot = this as Slot ?? (this as Component)?.Slot;
		if (slot == null)
		{
			return false;
		}

		return ReferenceEquals(slot, rootSlot) || slot.IsDescendantOf(rootSlot);
	}
}
