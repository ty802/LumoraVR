// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

[ComponentCategory("Physics")]
public class RespawnPlane : ImplementableComponent
{
    public readonly Sync<float2> Size = null!;
    public readonly Sync<bool> UseBounds = null!;
    public readonly Sync<float> Height = null!;
    public readonly Sync<color> VisualColor = null!;
    public readonly Sync<color> DebugColor = null!;
    public readonly Sync<bool> ShowVisual = null!;
    public readonly Sync<bool> ShowDebug = null!;
    public readonly Sync<float3> UserRespawnPosition = null!;

    // Four checks a second. Something under the plane is still under it a quarter second later, and
    // nothing here needs to be per frame. It used to ride the hook's ApplyChanges every frame and walk
    // the entire slot tree while it was at it: 40 ms a frame on a world with one imported avatar in
    // it. Now it asks the registry, which knows every RespawnData without looking. -xlinka
    private const double CheckInterval = 0.25;
    private double _nextCheck;

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
        double now = World?.Time.TotalTime ?? 0.0;
        if (now < _nextCheck)
            return;
        _nextCheck = now + CheckInterval;
        CheckFallen();
    }

    private void ApplyHeight()
    {
        var localPos = Slot.LocalPosition.Value;
        if (System.Math.Abs(localPos.y - Height.Value) > 0.0001f)
        {
            Slot.LocalPosition.Value = new float3(localPos.x, Height.Value, localPos.z);
        }
    }

    private void CheckFallen()
    {
        var world = World;
        if (world == null)
            return;
        float3 planePos = Slot.GlobalPosition;
        float2 half = Size.Value * 0.5f;
        bool useBounds = UseBounds.Value;
        // Each client puts its own user back; a remote user's root is not ours to move.
        var local = world.LocalUser;
        var localRoot = local?.Root?.Slot;
        if (localRoot != null && !localRoot.IsDestroyed && IsBelow(localRoot.GlobalPosition, planePos, half, useBounds))
            TeleportUser(local!, UserRespawnPosition.Value);
        var registry = RespawnRegistry.For(world);
        if (registry == null)
            return;
        var items = registry.Items;
        for (int i = 0; i < items.Count; i++)
        {
            var data = items[i];
            var slot = data?.Slot;
            if (slot == null || slot.IsDestroyed)
                continue;
            if (!IsBelow(slot.GlobalPosition, planePos, half, useBounds))
                continue;
            ResetToOrigin(slot, data!);
        }
    }

    private static bool IsBelow(in float3 pos, in float3 planePos, in float2 half, bool useBounds)
    {
        if (pos.y >= planePos.y)
            return false;
        if (!useBounds)
            return true;
        return pos.x >= planePos.x - half.x && pos.x <= planePos.x + half.x
            && pos.z >= planePos.z - half.y && pos.z <= planePos.z + half.y;
    }

    private static void TeleportUser(User user, float3 spawnPos)
    {
        var userRoot = user.Root;
        if (userRoot == null)
            return;
        var controller = userRoot.Slot.GetComponent<CharacterController>();
        if (controller != null)
            controller.Teleport(spawnPos);
        else
            userRoot.Slot.GlobalPosition = spawnPos;
        Logging.Logger.Debug($"RespawnPlane: Teleported user '{userRoot.ActiveUser?.UserName?.Value ?? "Unknown"}' to spawn");
    }

    private static void ResetToOrigin(Slot slot, RespawnData data)
    {
        var originalPos = data.OriginalPosition.Value;
        var originalRot = data.OriginalRotation.Value;
        var rigidBody = slot.GetComponent<RigidBody>();
        // A simulated body is put back by whoever simulates it; anyone else would only fight the
        // owner's pose for a frame.
        if (rigidBody != null && !rigidBody.IsSimulationOwner)
            return;
        Logging.Logger.Debug($"RespawnPlane: Reset '{slot.SlotName.Value}' from {slot.GlobalPosition} to {originalPos}");
        if (rigidBody != null)
        {
            rigidBody.Teleport(originalPos, originalRot);
            return;
        }
        slot.GlobalPosition = originalPos;
        slot.GlobalRotation = originalRot;
    }

    public void OnObjectEntered(Slot objectSlot)
    {
        if (objectSlot == null || objectSlot.IsDestroyed) return;
        var userRoot = objectSlot.GetComponentInParents<UserRoot>();
        if (userRoot != null)
        {
            var spawnPos = UserRespawnPosition.Value;
            userRoot.Slot.GlobalPosition = spawnPos;
            Logging.Logger.Debug($"RespawnPlane: Teleported user '{userRoot.ActiveUser?.UserName?.Value ?? "Unknown"}' to spawn");
            return;
        }
        var respawnData = objectSlot.GetComponent<RespawnData>();
        if (respawnData != null)
            ResetToOrigin(objectSlot, respawnData);
    }
}
