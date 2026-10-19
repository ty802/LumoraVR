// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Assets;

public sealed class ShaderUniformParam : SyncElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;
    public new readonly Sync<string> Name = new();
    public readonly Sync<ShaderUniformType> Type = new();
    public readonly Sync<float4> Value = new();
    public readonly Sync<float2> Range = new();
    public readonly Sync<bool> HasRange = new();
    public readonly Sync<bool> IsColor = new();
    public readonly AssetRef<TextureAsset> Texture = new();
    public readonly AssetRef<CubemapAsset> Cubemap = new();

    public override void Initialize(World world, IWorldElement? parent)
    {
        base.Initialize(world, parent);
        SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        // Shader uniform parameters sync their fields as separate SyncElements.
    }

    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        // No payload for the container element.
    }

    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        // Shader uniform parameters sync their fields as separate SyncElements.
    }

    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        // No payload for the container element.
    }

    protected override void InternalClearDirty()
    {
        // No internal dirty state to clear.
    }

    // Saved like the other composite entries, so a material's values and textures survive a save: the host's
    // rebuild after a load carries over only what the list already holds.
    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("Name", Name.Save(control));
        dictionary.Add("Type", Type.Save(control));
        dictionary.Add("Value", Value.Save(control));
        dictionary.Add("Range", Range.Save(control));
        dictionary.Add("HasRange", HasRange.Save(control));
        dictionary.Add("IsColor", IsColor.Save(control));
        dictionary.Add("Texture", Texture.Save(control));
        dictionary.Add("Cubemap", Cubemap.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("Name") is { } nameNode)
            Name.Load(nameNode, control);
        if (dictionary.TryGetNode("Type") is { } typeNode)
            Type.Load(typeNode, control);
        if (dictionary.TryGetNode("Value") is { } valueNode)
            Value.Load(valueNode, control);
        if (dictionary.TryGetNode("Range") is { } rangeNode)
            Range.Load(rangeNode, control);
        if (dictionary.TryGetNode("HasRange") is { } hasRangeNode)
            HasRange.Load(hasRangeNode, control);
        if (dictionary.TryGetNode("IsColor") is { } isColorNode)
            IsColor.Load(isColorNode, control);
        if (dictionary.TryGetNode("Texture") is { } textureNode)
            Texture.Load(textureNode, control);
        if (dictionary.TryGetNode("Cubemap") is { } cubemapNode)
            Cubemap.Load(cubemapNode, control);
    }

    public override object? GetValueAsObject() => Name.Value;
}
