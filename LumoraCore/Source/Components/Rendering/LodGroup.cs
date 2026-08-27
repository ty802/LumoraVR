// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.IO;
using Helio.UI;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components;

public sealed class LodLevel : SyncElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    // metres, measured from the group's own slot at unit scale, past which this level stops being
    // used and the next one takes over
    public readonly Sync<float> Distance = new();

    // everything renderable under each of them is included, so a level is normally one slot holding
    // one version of the model
    public readonly SyncRefList<Slot> Renderers = new();

    public override void Initialize(World world, IWorldElement? parent)
    {
        base.Initialize(world, parent);
        SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalClearDirty() { }

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("Distance", Distance.Save(control));
        dictionary.Add("Renderers", Renderers.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("Distance") is { } distanceNode)
            Distance.Load(distanceNode, control);
        if (dictionary.TryGetNode("Renderers") is { } renderersNode)
            Renderers.Load(renderersNode, control);
    }

    public override object? GetValueAsObject() => Distance.Value;
}

// Swaps between cheaper versions of the same object as the viewer gets further away.
//
// The switch is not simulated here. Every level is turned into a distance band and pushed down to
// the renderers once, and the renderer then does the per-frame distance test itself - which it is
// already doing for culling, so the LOD test is effectively free. Nothing in this component or its
// hook runs per frame; the bands are recomputed only when the group's fields change, when a renderer
// appears or disappears under a level slot, when the group is moved or rescaled, or when the LOD bias
// setting moves.
//
// Thresholds are DISTANCES, in metres, not screen fractions. Distance is what the renderer consumes,
// so it is the only unit that survives the trip without being converted through a field-of-view that
// differs per eye in VR and would have to be re-derived every frame to stay honest. To pick a
// distance from a screen fraction by hand: d = size / (2 * fraction * tan(fov / 2)), with size the
// object's height in metres. -xlinka
[ComponentCategory("Rendering")]
public class LodGroup : ImplementableComponent, ICustomInspectorUI
{
    // nearest first; order in the list is what matters, not the numbers - a level's band runs from
    // the previous level's distance to its own
    public readonly SyncList<LodLevel> Levels;

    // metres; 0 switches hard, non-zero makes the renderer draw both levels for that stretch, so it
    // costs what the overlap costs
    [Range(0f, 20f, "0.00")]
    public readonly Sync<float> CrossfadeMargin;

    public readonly Sync<bool> CullBeyondLast;

    public readonly Sync<bool> IgnoreScale;

    // the hook is the only listener
    public event System.Action? BandsInvalidated;

    private readonly List<Slot> _watched = new();

    public LodGroup()
    {
        Levels = new SyncList<LodLevel>();
        CrossfadeMargin = new Sync<float>(this, 0f);
        CullBeyondLast = new Sync<bool>(this, true);
        IgnoreScale = new Sync<bool>(this, false);
    }

    public override void OnStart()
    {
        base.OnStart();
        EngineSettings.Changed += OnSettingChanged;
        RefreshWatches();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        // A level slot may have been swapped for another, so the structure subscriptions have to
        // follow. Cheap: it is one pass over the levels, and only on a change pass.
        RefreshWatches();
    }

    public override void OnDestroy()
    {
        EngineSettings.Changed -= OnSettingChanged;
        ClearWatches();
        base.OnDestroy();
    }

    public LodLevel AddLevel(float distance, Slot? root = null)
    {
        var level = Levels.Add();
        level.Distance.Value = distance;
        if (root != null)
            level.Renderers.Add(root);
        return level;
    }

    // a group scaled up is physically bigger and so stays readable further out; the switch distances
    // have to move with it or a scaled instance pops to its cheap level while it still fills the screen
    public float DistanceScale
    {
        get
        {
            if (IgnoreScale.Value || Slot == null)
                return EngineSettings.LodBias;

            var scale = Slot.GlobalScale;
            float largest = System.Math.Max(System.Math.Abs(scale.x),
                System.Math.Max(System.Math.Abs(scale.y), System.Math.Abs(scale.z)));
            if (largest < 1e-4f)
                largest = 1f;
            return largest * EngineSettings.LodBias;
        }
    }

    // already scaled; Begin 0 means "from the camera", End 0 means "for ever", which is how the
    // renderer reads them too
    public (float Begin, float End) BandFor(int index)
    {
        int count = Levels.Count;
        if (index < 0 || index >= count)
            return (0f, 0f);

        float scale = DistanceScale;
        float begin = index == 0 ? 0f : System.Math.Max(0f, Levels[index - 1].Distance.Value) * scale;
        bool last = index == count - 1;
        float end = last && !CullBeyondLast.Value
            ? 0f
            : System.Math.Max(0f, Levels[index].Distance.Value) * scale;

        // A level whose distance sits at or below the previous one would be a band of zero width that
        // never draws. Widening it instead of dropping it keeps the authored order visible, so the
        // mistake shows up on screen rather than as a level that silently does nothing.
        if (end > 0f && end <= begin)
            end = begin + 0.01f;

        return (begin, end);
    }

    // metres, scaled the same way the distances are
    public float ScaledCrossfade => System.Math.Max(0f, CrossfadeMargin.Value) * DistanceScale;

    // WATCHES
    //
    // A renderer appearing or disappearing under a level slot changes which instances need the band,
    // and neither event reaches this component through the ordinary change pass. SubtreeStructureChanged
    // covers both without subscribing to every descendant. WorldTransformChanged covers the group being
    // moved or rescaled, which changes the distance scale. Both are deferred, per-frame-at-most events,
    // not polls.
    private void RefreshWatches()
    {
        var wanted = new List<Slot>();
        if (Slot != null)
            wanted.Add(Slot);
        foreach (var level in Levels)
        {
            foreach (var root in level.Renderers)
            {
                if (root != null && !root.IsDestroyed && !wanted.Contains(root))
                    wanted.Add(root);
            }
        }

        if (SameWatches(wanted))
            return;

        ClearWatches();
        foreach (var slot in wanted)
        {
            slot.SubtreeStructureChanged += OnStructureChanged;
            _watched.Add(slot);
        }

        if (Slot != null)
            Slot.WorldTransformChanged += OnTransformChanged;

        BandsInvalidated?.Invoke();
    }

    private bool SameWatches(List<Slot> wanted)
    {
        if (wanted.Count != _watched.Count)
            return false;
        for (int i = 0; i < wanted.Count; i++)
        {
            if (!ReferenceEquals(wanted[i], _watched[i]))
                return false;
        }
        return true;
    }

    private void ClearWatches()
    {
        foreach (var slot in _watched)
        {
            if (slot != null)
                slot.SubtreeStructureChanged -= OnStructureChanged;
        }
        _watched.Clear();

        if (Slot != null)
            Slot.WorldTransformChanged -= OnTransformChanged;
    }

    private void OnStructureChanged(Slot _) => BandsInvalidated?.Invoke();

    private void OnTransformChanged(Slot _)
    {
        if (!IgnoreScale.Value)
            BandsInvalidated?.Invoke();
    }

    private void OnSettingChanged()
    {
        var world = World;
        if (world == null || IsDestroyed)
            return;
        world.RunSynchronously(() =>
        {
            if (!IsDestroyed)
                BandsInvalidated?.Invoke();
        });
    }

    // DIAGNOSTICS
    //
    // The bands are the honest answer to "what did this group actually do": they are the numbers the
    // renderer was given. The level row is a snapshot from the local viewer's head at the moment the
    // inspector body was built, refreshed by LodGroupStats while the row exists.
    public void BuildInspectorBody(UIBuilder ui)
    {
        if (Levels.Count == 0)
        {
            InspectorStats.AddRow(ui, "Levels", "none, this group does nothing");
            return;
        }

        InspectorStats.AddRow(ui, "Distance scale", $"{DistanceScale:0.00}x (bias {EngineSettings.LodBias:0.00})");
        LodGroupStats.AddLiveLevelRow(ui, this);

        for (int i = 0; i < Levels.Count; i++)
        {
            var (begin, end) = BandFor(i);
            InspectorStats.AddRow(ui, $"Level {i}",
                $"{begin:0.0} m to {(end <= 0f ? "infinity" : $"{end:0.0} m")}, {CountRenderers(i)} renderers");
        }
    }

    public int CountRenderers(int index)
    {
        if (index < 0 || index >= Levels.Count)
            return 0;

        int count = 0;
        foreach (var root in Levels[index].Renderers)
        {
            if (root == null || root.IsDestroyed)
                continue;
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer != null && !renderer.IsDestroyed)
                    count++;
            }
        }
        return count;
    }

    // -1 when the group is culled there; recomputed from the same bands the renderer was given, so it
    // cannot drift from what is drawn
    public int LevelAt(float3 viewer)
    {
        if (Slot == null || Levels.Count == 0)
            return -1;

        float distance = (viewer - Slot.GlobalPosition).Length;
        for (int i = 0; i < Levels.Count; i++)
        {
            var (begin, end) = BandFor(i);
            if (distance >= begin && (end <= 0f || distance < end))
                return i;
        }
        return -1;
    }
}
