// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Godot;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Input;
using Lumora.Core.Math;
using Lumora.Godot.Hooks;

namespace Lumora.Godot.UI;

/// <summary>
/// Temporary authoring session used to finalize an imported avatar.
/// </summary>
internal sealed class AvatarCreatorSession
{
    private const string CreatorMarkersRootName = "AvatarCreatorMarkers";
    private const string ReferenceRootName = "AvatarReferences";
    private const float PedestalBaseRadius = 0.50f;
    private const float PedestalBaseHeight = 0.10f;
    private const float PedestalColumnRadius = 0.12f;
    private const float PedestalColumnHeight = 0.34f;
    private const float PedestalTopRadius = 0.40f;
    private const float PedestalTopHeight = 0.06f;
    private const float PedestalTopCenterY = 0.47f;
    private const float PedestalSurfaceY = PedestalTopCenterY + (PedestalTopHeight * 0.5f);
    private const float PedestalPlacementOffset = 0.01f;

    private readonly Slot _avatarSlot;
    private readonly Slot _setupParent;
    private readonly Slot? _originalParent;
    private readonly float3 _originalGlobalPosition;
    private readonly floatQ _originalGlobalRotation;

    private Slot? _creatorRoot;
    private Slot? _avatarMount;
    private Slot? _markersRoot;
    private Slot? _leftMarker;
    private Slot? _rightMarker;
    private Slot? _viewMarker;

    public AvatarCreatorSession(Slot avatarSlot, Slot setupParent)
    {
        _avatarSlot = avatarSlot;
        _setupParent = setupParent;
        _originalParent = avatarSlot.Parent ?? setupParent;
        _originalGlobalPosition = avatarSlot.GlobalPosition;
        _originalGlobalRotation = avatarSlot.GlobalRotation;
    }

    public bool IsActive => _creatorRoot != null && !_creatorRoot.IsDestroyed;

    public bool Matches(Slot avatarSlot)
    {
        return _avatarSlot == avatarSlot;
    }

    public bool CanOpen(out string message)
    {
        message = string.Empty;

        if (_avatarSlot == null || _avatarSlot.IsDestroyed)
        {
            message = "Imported avatar is no longer available.";
            return false;
        }

        var draft = _avatarSlot.GetComponent<AvatarDraft>();
        if (draft == null)
        {
            message = "Imported avatar is missing draft metadata.";
            return false;
        }

        if (draft.IsFinalized.Value)
        {
            message = "Avatar has already been created.";
            return false;
        }

        var modelData = _avatarSlot.GetComponent<ModelData>();
        if (modelData != null && !modelData.IsLoaded.Value)
        {
            message = "Avatar is still loading. Try again in a moment.";
            return false;
        }

        if (!draft.RefreshResolvedReferences())
        {
            message = "Avatar rig is not ready yet.";
            return false;
        }

        return true;
    }

    public bool Open(out string message)
    {
        if (IsActive)
        {
            message = "Creator is already open.";
            return true;
        }

        if (!CanOpen(out message))
            return false;

        var existing = _setupParent.FindChild($"{GetDisplayName()}_Creator", recursive: false);
        if (existing != null && !existing.IsDestroyed)
            existing.Destroy();

        _creatorRoot = _setupParent.AddSlot($"{GetDisplayName()}_Creator");
        _creatorRoot.GlobalPosition = _originalGlobalPosition;
        _creatorRoot.GlobalRotation = floatQ.Identity;
        _creatorRoot.AttachComponent<Grabbable>();

        var pedestalCollider = _creatorRoot.AttachComponent<BoxCollider>();
        pedestalCollider.Size.Value = new float3(1.20f, 0.24f, 1.20f);
        pedestalCollider.Offset.Value = new float3(0f, 0.12f, 0f);

        AvatarSetupPedestalHelper.CreatePedestalPart(_creatorRoot, "Base", new float3(0f, PedestalBaseHeight * 0.5f, 0f), PedestalBaseRadius, PedestalBaseHeight, new colorHDR(0.12f, 0.14f, 0.20f, 1f));
        AvatarSetupPedestalHelper.CreatePedestalPart(_creatorRoot, "Column", new float3(0f, 0.27f, 0f), PedestalColumnRadius, PedestalColumnHeight, new colorHDR(0.20f, 0.24f, 0.34f, 1f));
        AvatarSetupPedestalHelper.CreatePedestalPart(_creatorRoot, "Top", new float3(0f, PedestalTopCenterY, 0f), PedestalTopRadius, PedestalTopHeight, new colorHDR(0.28f, 0.34f, 0.48f, 1f));

        _avatarMount = _creatorRoot.AddSlot("AvatarMount");
        _avatarMount.LocalPosition.Value = new float3(0f, PedestalSurfaceY, 0f);
        _avatarMount.LocalRotation.Value = floatQ.Identity;

        _avatarSlot.SetParent(_avatarMount, preserveGlobalTransform: true);
        _avatarSlot.GlobalRotation = _originalGlobalRotation;
        _avatarSlot.GlobalPosition = _avatarMount.GlobalPosition;
        AlignAvatarToPedestal();

        BuildMarkers();

        message = "Move the view and grip markers, then press Create Avatar.";
        return true;
    }

    public bool Finalize(out string message)
    {
        message = string.Empty;

        if (!IsActive || _markersRoot == null || _markersRoot.IsDestroyed)
        {
            message = "Open the creator before finalizing the avatar.";
            return false;
        }

        var draft = _avatarSlot.GetComponent<AvatarDraft>();
        if (draft == null)
        {
            message = "Avatar draft metadata is missing.";
            return false;
        }

        if (!draft.RefreshResolvedReferences() || draft.Skeleton.Target == null || draft.Rig.Target == null)
        {
            message = "Avatar rig is not ready yet.";
            return false;
        }

        var avatarRoot = _avatarSlot.GetComponent<AvatarRoot>() ?? _avatarSlot.AttachComponent<AvatarRoot>();
        var descriptor = _avatarSlot.GetComponent<AvatarDescriptor>() ?? _avatarSlot.AttachComponent<AvatarDescriptor>();

        descriptor.Root.Target = avatarRoot;
        descriptor.Skeleton.Target = draft.Skeleton.Target;
        descriptor.Rig.Target = draft.Rig.Target;
        descriptor.IsFinalized.Value = true;
        descriptor.HasFeetCalibration.Value = false;
        descriptor.HasPelvisCalibration.Value = false;

        draft.Descriptor.Target = descriptor;
        draft.IsFinalized.Value = true;

        var referenceRoot = _avatarSlot.FindChild(ReferenceRootName, recursive: false);
        if (referenceRoot != null && !referenceRoot.IsDestroyed)
            referenceRoot.Destroy();

        referenceRoot = _avatarSlot.AddSlot(ReferenceRootName);
        referenceRoot.LocalPosition.Value = float3.Zero;
        referenceRoot.LocalRotation.Value = floatQ.Identity;

        CreateReferencePoint(referenceRoot, descriptor, AvatarReferenceKind.View, "View", _viewMarker);
        CreateReferencePoint(referenceRoot, descriptor, AvatarReferenceKind.LeftHandGrip, "LeftHandGrip", _leftMarker);
        CreateReferencePoint(referenceRoot, descriptor, AvatarReferenceKind.RightHandGrip, "RightHandGrip", _rightMarker);

        var ikAvatar = _avatarSlot.GetComponentInChildren<GodotIKAvatar>();
        if (ikAvatar == null)
        {
            var ikSlot = _avatarSlot.AddSlot("IK");
            ikAvatar = ikSlot.AttachComponent<GodotIKAvatar>();
        }

        ikAvatar.Skeleton.Target = draft.Skeleton.Target;
        ikAvatar.Enabled.Value = true;

        var vrikAvatar = _avatarSlot.GetComponent<VRIKAvatar>() ?? _avatarSlot.AttachComponent<VRIKAvatar>();
        vrikAvatar.Descriptor.Target = descriptor;

        CloseSession(restoreOriginalTransform: true);

        message = "Avatar created. Press Equip Avatar to use it now.";
        return true;
    }

    public void Dispose()
    {
        CloseSession(restoreOriginalTransform: true);
    }

    private void BuildMarkers()
    {
        _markersRoot = _avatarSlot.FindChild(CreatorMarkersRootName, recursive: false);
        if (_markersRoot != null && !_markersRoot.IsDestroyed)
            _markersRoot.Destroy();

        _markersRoot = _avatarSlot.AddSlot(CreatorMarkersRootName);
        _markersRoot.LocalPosition.Value = float3.Zero;
        _markersRoot.LocalRotation.Value = floatQ.Identity;

        _leftMarker = AvatarSetupPedestalHelper.CreateSetupMarker(_markersRoot, "LeftHandGrip", new colorHDR(0.25f, 0.65f, 1.0f, 0.9f), new float3(-0.22f, 1.25f, 0.08f));
        _rightMarker = AvatarSetupPedestalHelper.CreateSetupMarker(_markersRoot, "RightHandGrip", new colorHDR(1.0f, 0.45f, 0.25f, 0.9f), new float3(0.22f, 1.25f, 0.08f));
        _viewMarker = AvatarSetupPedestalHelper.CreateSetupMarker(_markersRoot, "View", new colorHDR(0.45f, 1.0f, 0.65f, 0.9f), new float3(0f, 1.58f, 0.06f));

        var rig = _avatarSlot.GetComponentInChildren<BipedRig>();
        var leftHandBone = rig?.TryGetBone(BodyNode.LeftHand);
        var rightHandBone = rig?.TryGetBone(BodyNode.RightHand);
        var headBone = rig?.TryGetBone(BodyNode.Head);

        AvatarSetupPedestalHelper.ApplyMarkerFromBone(_avatarSlot, _leftMarker, leftHandBone, float3.Backward);
        AvatarSetupPedestalHelper.ApplyMarkerFromBone(_avatarSlot, _rightMarker, rightHandBone, float3.Backward);
        AvatarSetupPedestalHelper.ApplyMarkerFromBone(_avatarSlot, _viewMarker, headBone, float3.Backward, 0.06f);
    }

    private void CreateReferencePoint(
        Slot referenceRoot,
        AvatarDescriptor descriptor,
        AvatarReferenceKind kind,
        string name,
        Slot? marker)
    {
        if (marker == null || marker.IsDestroyed)
            return;

        var referenceSlot = referenceRoot.AddSlot(name);
        referenceSlot.LocalPosition.Value = marker.LocalPosition.Value;
        referenceSlot.LocalRotation.Value = marker.LocalRotation.Value;

        var point = referenceSlot.AttachComponent<AvatarReferencePoint>();
        point.Kind.Value = kind;
        descriptor.SetReferenceSlot(kind, referenceSlot);
    }

    private void CloseSession(bool restoreOriginalTransform)
    {
        if (_markersRoot != null && !_markersRoot.IsDestroyed)
        {
            _markersRoot.Destroy();
            _markersRoot = null;
        }

        if (_avatarMount != null && !_avatarMount.IsDestroyed && _avatarSlot.Parent == _avatarMount)
        {
            _avatarSlot.SetParent(_originalParent ?? _setupParent, preserveGlobalTransform: true);
            if (restoreOriginalTransform)
            {
                _avatarSlot.GlobalPosition = _originalGlobalPosition;
                _avatarSlot.GlobalRotation = _originalGlobalRotation;
            }
        }

        if (_creatorRoot != null && !_creatorRoot.IsDestroyed)
        {
            _creatorRoot.Destroy();
            _creatorRoot = null;
        }

        _avatarMount = null;
        _leftMarker = null;
        _rightMarker = null;
        _viewMarker = null;
    }

    private string GetDisplayName()
    {
        var name = _avatarSlot.SlotName?.Value;
        return string.IsNullOrWhiteSpace(name) ? "Avatar" : name;
    }

    private void AlignAvatarToPedestal()
    {
        if (_avatarMount == null || _avatarMount.IsDestroyed)
            return;

        if (!TryComputeAvatarWorldBounds(_avatarSlot, out var worldBounds))
        {
            _avatarSlot.GlobalPosition = _avatarMount.GlobalPosition;
            return;
        }

        var boundsCenter = worldBounds.Position + (worldBounds.Size * 0.5f);
        var target = ToGodotVector(_avatarMount.GlobalPosition);
        var translation = new Vector3(
            target.X - boundsCenter.X,
            (target.Y + PedestalPlacementOffset) - worldBounds.Position.Y,
            target.Z - boundsCenter.Z);

        _avatarSlot.GlobalPosition += new float3(translation.X, translation.Y, translation.Z);
    }

    private static bool TryComputeAvatarWorldBounds(Slot targetSlot, out Aabb worldBounds)
    {
        worldBounds = default;

        if (targetSlot.Hook is not SlotHook slotHook)
            return false;

        var targetNode = slotHook.ForceGetNode3D();
        if (!GodotObject.IsInstanceValid(targetNode))
            return false;

        bool hasBounds = false;
        var stack = new Stack<Node>();
        stack.Push(targetNode);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (Node child in current.GetChildren())
                stack.Push(child);

            if (current is not Node3D node3D || !node3D.Visible)
                continue;

            if (!TryGetLocalAabb(node3D, out var localBounds))
                continue;

            var transformed = TransformAabb(node3D.GlobalTransform, localBounds);
            AddAabb(ref worldBounds, transformed, ref hasBounds);
        }

        return hasBounds;
    }

    private static bool TryGetLocalAabb(Node3D node, out Aabb localBounds)
    {
        switch (node)
        {
            case MeshInstance3D meshInstance when meshInstance.Mesh != null:
                localBounds = meshInstance.GetAabb();
                return localBounds.Size != Vector3.Zero;

            default:
                localBounds = default;
                return false;
        }
    }

    private static Aabb TransformAabb(Transform3D transform, Aabb source)
    {
        var corners = GetAabbCorners(source);
        var transformedMin = transform * corners[0];
        var transformedMax = transformedMin;

        for (int i = 1; i < corners.Length; i++)
        {
            var point = transform * corners[i];
            transformedMin = transformedMin.Min(point);
            transformedMax = transformedMax.Max(point);
        }

        return new Aabb(transformedMin, transformedMax - transformedMin);
    }

    private static Vector3[] GetAabbCorners(Aabb bounds)
    {
        var min = bounds.Position;
        var max = bounds.End;
        return new[]
        {
            new Vector3(min.X, min.Y, min.Z),
            new Vector3(max.X, min.Y, min.Z),
            new Vector3(min.X, max.Y, min.Z),
            new Vector3(max.X, max.Y, min.Z),
            new Vector3(min.X, min.Y, max.Z),
            new Vector3(max.X, min.Y, max.Z),
            new Vector3(min.X, max.Y, max.Z),
            new Vector3(max.X, max.Y, max.Z)
        };
    }

    private static void AddAabb(ref Aabb aggregate, Aabb next, ref bool hasBounds)
    {
        if (!hasBounds)
        {
            aggregate = next;
            hasBounds = true;
            return;
        }

        var min = aggregate.Position.Min(next.Position);
        var max = aggregate.End.Max(next.End);
        aggregate = new Aabb(min, max - min);
    }

    private static Vector3 ToGodotVector(float3 value)
    {
        return new Vector3(value.x, value.y, value.z);
    }
}
