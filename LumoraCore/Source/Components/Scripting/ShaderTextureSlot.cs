// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Helio.UI;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Components.UI;
using Lumora.Core.Scripting;

namespace Lumora.Core.Components.Scripting;

// A shader graph card's texture slot: the texture a node reads, as a picture and a name. It is the
// material's property for that node, the same one the material panel's texture row shows, so a texture
// given here is the material's for everyone. Hold a texture's card from an inspector, or the textured object
// itself, and press the slot or let go over it. The picture follows the property whoever changes it. Lives
// on the local card. -xlinka
[ComponentCategory("Hidden")]
public sealed class ShaderTextureSlot : Component, IProxyReceiver
{
    public ShaderGraphSource? Source { get; set; }
    public string Uniform { get; set; } = string.Empty;
    public RawImage? Picture { get; set; }
    public Text? Label { get; set; }

    private IAssetProvider<TextureAsset>? _shown;
    private bool _drawn;

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        var current = Source is { IsDestroyed: false } source ? source.TextureFor(Uniform) : null;
        if (_drawn && ReferenceEquals(current, _shown))
            return;
        _drawn = true;
        _shown = current;
        if (Picture is { IsDestroyed: false } picture)
        {
            // Empty shows the checker behind it rather than the white an empty picture samples.
            picture.Texture.Target = current!;
            picture.Enabled.Value = current != null;
        }
        if (Label is { IsDestroyed: false } label)
        {
            label.Content.Value = current is Component component ? Shorten(component.Slot?.SlotName.Value ?? "texture", 18)
                : Source == null ? "no material" : "drop texture";
            label.Color.Value = current != null ? DashTheme.Text : DashTheme.TextMuted;
        }
    }

    public bool TryReceiveProxy(IReadOnlyList<IGrabbable> held, Grabber grabber)
    {
        var source = Source;
        if (source == null || source.IsDestroyed || string.IsNullOrEmpty(Uniform))
            return false;
        for (int i = 0; i < held.Count; i++)
        {
            if (held[i] is not Component component || component.Slot == null)
                continue;
            var proxy = component.Slot.GetComponent<ReferenceProxy>();
            var texture = proxy?.Target.Target is { } element ? TextureIn(element) : TextureIn(component.Slot);
            if (texture == null)
                continue;

            var before = source.Material.Target?.Parameters.FirstOrDefault(p => p.Name.Value == Uniform)?.Texture.Value ?? default;
            ShaderUniformParam? param;
            try
            {
                param = source.AssignTexture(Uniform, texture);
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            if (param == null)
                return false;
            InspectorUndo.Record(this, new FieldEditUndoBatch(param.Texture, before, param.Texture.Value, ScriptGraphEditing.ValueStep));
            proxy?.Consume(held[i], grabber);
            return true;
        }
        return false;
    }

    // The texture something stands for: itself, what it points at, or one on it or just under it, where an
    // imported picture keeps its own.
    public static IAssetProvider<TextureAsset>? TextureIn(IWorldElement element)
    {
        if (element is IAssetProvider<TextureAsset> texture)
            return texture;
        if (element is ISyncRef reference && reference.Target is IAssetProvider<TextureAsset> target)
            return target;
        var slot = element as Slot ?? (element as Component)?.Slot;
        if (slot == null)
            return null;
        foreach (var candidate in slot.Components)
        {
            if (candidate is IAssetProvider<TextureAsset> found)
                return found;
        }
        foreach (var child in slot.Children)
        {
            foreach (var candidate in child.Components)
            {
                if (candidate is IAssetProvider<TextureAsset> found)
                    return found;
            }
        }
        return null;
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 3)] + "...";
}
