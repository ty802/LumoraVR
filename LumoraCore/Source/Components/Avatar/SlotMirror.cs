using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Each frame, drives a target slot's transform to match this slot's global position/rotation.
/// Allows a slot anywhere in the hierarchy to "virtually follow" this slot without being a
/// real child of it — useful for bridging VR tracker positions into avatar bone proxies.
/// </summary>
[ComponentCategory("Avatar")]
public class SlotMirror : Component
{
    /// <summary>The slot whose transform will be driven to match this slot.</summary>
    public SyncRef<Slot> DriveTarget { get; private set; }

    /// <summary>Also mirror scale (default false).</summary>
    public Sync<bool> MirrorScale { get; private set; }

    public override void OnAwake()
    {
        base.OnAwake();
        DriveTarget = new SyncRef<Slot>(this, null);
        MirrorScale = new Sync<bool>(this, false);
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        var target = DriveTarget.Target;
        if (target == null || target.IsDestroyed) return;

        // Convert this slot's global position to the target slot's parent-local space.
        var parent = target.Parent;
        if (parent != null)
        {
            target.LocalPosition.Value = parent.GlobalPointToLocal(Slot.GlobalPosition);
            target.LocalRotation.Value = parent.GlobalRotation.Inverse * Slot.GlobalRotation;
        }
        else
        {
            target.LocalPosition.Value = Slot.GlobalPosition;
            target.LocalRotation.Value = Slot.GlobalRotation;
        }

        if (MirrorScale.Value)
            target.LocalScale.Value = Slot.LocalScale.Value;
    }
}
