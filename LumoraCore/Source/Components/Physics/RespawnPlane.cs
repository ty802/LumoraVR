// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

[ComponentCategory("Physics")]
public class RespawnPlane : ImplementableComponent
{
    // X/Z dimensions
    public readonly Sync<float2> Size = null!;

    public readonly Sync<bool> UseBounds = null!;

    public readonly Sync<float> Height = null!;

    public readonly Sync<color> VisualColor = null!;

    public readonly Sync<color> DebugColor = null!;

    public readonly Sync<bool> ShowVisual = null!;

    public readonly Sync<bool> ShowDebug = null!;

    public readonly Sync<float3> UserRespawnPosition = null!;

    public override void OnAwake()
    {
        base.OnAwake();
        Size.OnChanged += _ => RunApplyChanges();
        UseBounds.OnChanged += _ => RunApplyChanges();
        Height.OnChanged += _ =>
        {
            ApplyHeight();
            RunApplyChanges();
        };
        VisualColor.OnChanged += _ => RunApplyChanges();
        DebugColor.OnChanged += _ => RunApplyChanges();
        ShowVisual.OnChanged += _ => RunApplyChanges();
        ShowDebug.OnChanged += _ => RunApplyChanges();
    }

    public override void OnInit()
    {
        base.OnInit();
        Size.Value = new float2(100f, 100f);
        UseBounds.Value = true;
        Height.Value = -20f;
        VisualColor.Value = new color(1f, 0.3f, 0.3f, 0.25f);
        DebugColor.Value = new color(0.9f, 0.2f, 1f, 1f);
        ShowVisual.Value = true;
        ShowDebug.Value = true;
        UserRespawnPosition.Value = new float3(0f, 1f, 0f);
        ApplyHeight();
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        // Trigger hook update every frame to check for fallen objects
        RunApplyChanges();
    }

    private void ApplyHeight()
    {
        var localPos = Slot.LocalPosition.Value;
        if (System.Math.Abs(localPos.y - Height.Value) > 0.0001f)
        {
            Slot.LocalPosition.Value = new float3(localPos.x, Height.Value, localPos.z);
        }
    }

    // called by the hook when an object enters the plane
    public void OnObjectEntered(Slot objectSlot)
    {
        if (objectSlot == null || objectSlot.IsDestroyed) return;

        var userRoot = objectSlot.GetComponentInParents<UserRoot>();
        if (userRoot != null)
        {
            var spawnPos = UserRespawnPosition.Value;
            userRoot.Slot.GlobalPosition = spawnPos;
            Logging.Logger.Log($"RespawnPlane: Teleported user '{userRoot.ActiveUser?.UserName?.Value ?? "Unknown"}' to spawn");
            return;
        }

        var respawnData = objectSlot.GetComponent<RespawnData>();
        if (respawnData != null)
        {
            objectSlot.GlobalPosition = respawnData.OriginalPosition.Value;
            objectSlot.GlobalRotation = respawnData.OriginalRotation.Value;

            var rigidBody = objectSlot.GetComponent<RigidBody>();
            if (rigidBody != null)
            {
                rigidBody.LinearVelocity.Value = float3.Zero;
                rigidBody.AngularVelocity.Value = float3.Zero;
            }

            Logging.Logger.Log($"RespawnPlane: Reset '{objectSlot.SlotName.Value}' to original position");
        }
    }
}

// attach to objects that should respawn when hitting the RespawnPlane
[ComponentCategory("Physics")]
public class RespawnData : Component
{
    public readonly Sync<float3> OriginalPosition = null!;
    public readonly Sync<floatQ> OriginalRotation = null!;

    public override void OnAwake()
    {
        base.OnAwake();
    }

    public void StoreCurrentPosition()
    {
        OriginalPosition.Value = Slot.GlobalPosition;
        OriginalRotation.Value = Slot.GlobalRotation;
    }
}
