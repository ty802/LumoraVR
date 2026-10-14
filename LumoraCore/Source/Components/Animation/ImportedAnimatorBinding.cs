// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Core.Assets;
using Lumora.Core.Assets.Interop.Unity;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components;

// Number, Boolean: one value. Position, Rotation, Scale: an object's transform. BlendShape: a mesh shape
// by name. Color and Vector: a material property, Property naming it without its channel ("material._Color")
// and each of its four channels read from its own curve. Material: a renderer's material slot, swapped to
// the material whose package key the animation names.
public enum ImportedAnimatorChannel { Number, Boolean, Position, Rotation, Scale, BlendShape, Color, Vector, Material }

// Source key and destination are saved independently: a reloaded clip cannot silently bind to a
// different object merely because tracks or the model's child slots were reordered.
public sealed class ImportedAnimatorBinding : SyncElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;
    public readonly Sync<string> Path = new(), Property = new(), Script = new();
    public readonly Sync<int> ClassId = new();
    public readonly Sync<ImportedAnimatorChannel> Channel = new();
    public readonly AnyFieldDrive Target = new();
    public readonly SyncRef<SkinnedMeshRenderer> Renderer = new();
    public readonly Sync<float4> Rest = new();
    public readonly DriveRef<IAssetProvider<MaterialAsset>> MaterialSlot = new();
    public readonly SyncAssetList<MaterialAsset> Materials = new();
    public readonly SyncFieldList<string> MaterialKeys = new();
    public UnityBinding Key => new(Path.Value ?? "", ClassId.Value, Property.Value ?? "", string.IsNullOrEmpty(Script.Value) ? null : Script.Value);

    public override void Initialize(World world, IWorldElement? parent)
    {
        base.Initialize(world, parent);
        SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
    }
    protected override void InternalEncodeFull(BinaryWriter w, BinaryMessageBatch b) { }
    protected override void InternalDecodeFull(BinaryReader r, BinaryMessageBatch b) { }
    protected override void InternalEncodeDelta(BinaryWriter w, BinaryMessageBatch b) { }
    protected override void InternalDecodeDelta(BinaryReader r, BinaryMessageBatch b) { }
    protected override void InternalClearDirty() { }
    public override object? GetValueAsObject() => Property.Value;
    public override DataTreeNode Save(SaveControl control)
    {
        var d = new DataTreeDictionary();
        d.Add("Path", Path.Save(control)); d.Add("Property", Property.Save(control)); d.Add("Script", Script.Save(control));
        d.Add("ClassId", ClassId.Save(control)); d.Add("Channel", Channel.Save(control)); d.Add("Target", Target.Save(control));
        d.Add("Renderer", Renderer.Save(control)); d.Add("Rest", Rest.Save(control));
        if (Channel.Value == ImportedAnimatorChannel.Material)
        {
            d.Add("MaterialSlot", MaterialSlot.Save(control)); d.Add("Materials", Materials.Save(control));
            d.Add("MaterialKeys", MaterialKeys.Save(control));
        }
        return d;
    }
    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary d) return;
        if (d.TryGetNode("Path") is { } p) Path.Load(p, control);
        if (d.TryGetNode("Property") is { } v) Property.Load(v, control);
        if (d.TryGetNode("Script") is { } s) Script.Load(s, control);
        if (d.TryGetNode("ClassId") is { } i) ClassId.Load(i, control);
        if (d.TryGetNode("Channel") is { } c) Channel.Load(c, control);
        if (d.TryGetNode("Target") is { } t) Target.Load(t, control);
        if (d.TryGetNode("Renderer") is { } r) Renderer.Load(r, control);
        if (d.TryGetNode("Rest") is { } b) Rest.Load(b, control);
        if (d.TryGetNode("MaterialSlot") is { } m) MaterialSlot.Load(m, control);
        if (d.TryGetNode("Materials") is { } ms) Materials.Load(ms, control);
        if (d.TryGetNode("MaterialKeys") is { } mk) MaterialKeys.Load(mk, control);
    }
}
