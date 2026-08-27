// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core.Components.Magnets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// Stamped onto every copy a dispenser produces. It is what makes the dispenser's live count add
// up, and it is what puts a copy away again when it is set back down where it came from.
//
// Attached to the copy's GRABBABLE slot, not its root, because that is where the release edge
// arrives from - a mark on a root whose grabbable sits on a child would never hear anything. The
// root is remembered separately so a put-back removes the whole copy rather than the one branch
// that happened to carry the grab. -xlinka
[ComponentCategory("Interaction/Grabbables")]
[SingleInstancePerSlot]
public class GrabSpawnMark : GrabEventBehaviour, ICustomInspectorUI
{
    public readonly SyncRef<GrabSpawnerBase> Source;

    // The root of the copy, which is what a put-back removes.
    public readonly SyncRef<Slot> Instance;

    private GrabSpawnerBase? _registered;

    public GrabSpawnMark()
    {
        Source = new SyncRef<GrabSpawnerBase>(this);
        Instance = new SyncRef<Slot>(this);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        // Runs on network-decoded instances too, unlike OnInit. On a remote peer the reference
        // usually resolves after this component exists, so the count is joined on the edge rather
        // than only at start.
        Source.OnTargetChange += _ => Rejoin();
    }

    public override void OnStart()
    {
        base.OnStart();
        Rejoin();
    }

    public override void OnDestroy()
    {
        Leave();
        base.OnDestroy();
    }

    private void Rejoin()
    {
        var source = Source.Target;
        if (ReferenceEquals(source, _registered))
            return;
        Leave();
        if (source == null || source.IsDestroyed)
            return;
        source.RegisterInstance(this);
        _registered = source;
    }

    private void Leave()
    {
        _registered?.UnregisterInstance(this);
        _registered = null;
    }

    protected override void OnReleased(Grabbable carrier)
    {
        var source = Source.Target;
        if (source == null || source.IsDestroyed || !source.DestroyOnReturn.Value)
            return;

        // Same one-beat wait the magnets use, and for the same reason: a socket or a drop target
        // that has just claimed this copy gets to keep it, and putting it back is only the answer
        // when nothing else wanted it. Two updates rather than one, because the magnet's own
        // resolve runs during the update pass of the beat after the release.
        RunInUpdates(2, ResolveReturn);
    }

    private void ResolveReturn()
    {
        var source = Source.Target;
        var root = Instance.Target ?? Slot;
        if (source == null || source.IsDestroyed || root == null || root.IsDestroyed)
            return;
        if (Carrier?.IsGrabbed == true)
            return;
        if (MagnetHelper.IsSocketed(root) || MagnetHelper.IsSocketed(Slot))
            return;

        var home = source.Slot;
        if (home == null || home.IsDestroyed)
            return;

        float radius = source.ReturnRadius.Value;
        if (radius <= 0f)
            return;
        if (float3.DistanceSquared(root.GlobalPosition, home.GlobalPosition) > radius * radius)
            return;

        // Not undoable. Putting something back on the shelf is the shelf taking it, not an edit -
        // and the dispenser will hand out another one the moment it is asked, so there is nothing
        // to restore that a second grip would not produce for free.
        root.Destroy();
    }

    public void BuildInspectorBody(UIBuilder ui)
    {
        var source = Source.Target;
        InspectorStats.AddRow(ui, "Dispenser", source?.Slot?.SlotName.Value ?? "orphaned");
        InspectorStats.AddRow(ui, "Counted", _registered != null ? "yes" : "no");
        InspectorStats.AddRow(ui, "Put-back",
            source == null ? "n/a" : source.DestroyOnReturn.Value ? $"within {source.ReturnRadius.Value:0.##} m" : "off");
    }
}
