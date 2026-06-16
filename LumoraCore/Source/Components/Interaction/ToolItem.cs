// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

[ComponentCategory("Interaction/Tools")]
public abstract class ToolItem : Component
{
    public readonly SyncRef<Slot> TipReference = new();
    public readonly Sync<bool> BlockGripEquip = new();
    public readonly Sync<bool> BlockRemoteEquip = new();
    public readonly Sync<string> EquipName = new();
    public readonly SyncRef<Tool> OverrideActiveTool = new();

    public Tool? ActiveTool => OverrideActiveTool.Target ?? Slot.GetComponentInParents<Tool>();

    public bool IsEquipped => ActiveTool != null;

    public float3 Tip => Slot.LocalPointToGlobal(LocalTip);

    public virtual float3 TipForward => TipReference.Target?.Forward ?? Slot.Forward;

    public virtual float3 LocalTip
    {
        get
        {
            if (TipReference.Target == null)
            {
                return DefaultLocalTip;
            }

            if (ReferenceEquals(TipReference.Target.Parent, Slot))
            {
                return TipReference.Target.LocalPosition.Value;
            }

            return Slot.GlobalPointToLocal(TipReference.Target.GlobalPosition);
        }
    }

    protected virtual float3 DefaultLocalTip => float3.Zero;

    public virtual float3 InteractionOrigin => ActiveTool?.Laser?.RayOrigin ?? Tip;

    public virtual float3 InteractionDirection => ActiveTool?.Laser?.RayDirection ?? TipForward;

    public virtual bool UsesLaser => false;

    public virtual bool UsesLaserGrip => false;

    public virtual bool BlocksPrimaryWhenTouching => true;

    public virtual bool UsesSecondary => true;

    public virtual bool CanUseWhenHolding => false;

    public virtual bool IsInUse => ActiveTool?.PrimaryHeld == true;

    internal void SetActiveTool(Tool? tool)
    {
        OverrideActiveTool.Target = tool!;
    }

    public virtual bool UpdateTool(float primaryStrength, float2 secondaryAxis)
    {
        return false;
    }

    public virtual bool IsInteractionTarget(Slot target)
    {
        return true;
    }

    public virtual bool IsMovingTarget(Slot target)
    {
        return false;
    }

    public virtual float3? OverrideTargetPoint(float3 origin, float3 direction)
    {
        return null;
    }

    public virtual bool OnPrimaryPress()
    {
        return false;
    }

    public virtual bool OnPrimaryHold()
    {
        return false;
    }

    public virtual bool OnPrimaryRelease()
    {
        return false;
    }

    public virtual bool OnSecondaryPress()
    {
        return false;
    }

    public virtual bool OnSecondaryHold()
    {
        return false;
    }

    public virtual bool OnSecondaryRelease()
    {
        return false;
    }

    public virtual bool OnGrabbing(bool isLaserGrab)
    {
        return false;
    }

    public virtual void OnEquipped()
    {
    }

    public virtual void OnDequipped()
    {
    }
}
