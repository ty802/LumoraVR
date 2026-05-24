// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Helio.UI;

public sealed class MaterialCloneCache
{
    private readonly Slot _root;
    private readonly Dictionary<ClipMaterialKey, Entry> _clipMaterials = new();
    private int _frame;

    public MaterialCloneCache(Slot owner)
    {
        _root = owner.AddLocalSlot("MaterialCloneCache");
        _frame = 1;
    }

    public void BeginFrame()
    {
        _frame++;
        if (_frame == int.MaxValue)
        {
            _frame = 1;
            foreach (var entry in _clipMaterials.Values)
            {
                entry.LastUsedFrame = 0;
            }
        }
    }

    public IAssetProvider<MaterialAsset>? GetClippedMaterial(IAssetProvider<MaterialAsset>? source, in Rect clipRect)
    {
        if (source == null || source.IsDestroyed)
        {
            return null;
        }

        return source switch
        {
            UIUnlitMaterial unlit => GetClippedUIUnlit(unlit, clipRect),
            UITextMaterial text => GetClippedUIText(text, clipRect),
            _ => source,
        };
    }

    public void EndFrame()
    {
        List<ClipMaterialKey>? remove = null;
        foreach (var pair in _clipMaterials)
        {
            if (pair.Value.LastUsedFrame == _frame)
            {
                continue;
            }

            remove ??= new List<ClipMaterialKey>();
            remove.Add(pair.Key);
        }

        if (remove == null)
        {
            return;
        }

        foreach (var key in remove)
        {
            var entry = _clipMaterials[key];
            entry.Slot.Destroy();
            _clipMaterials.Remove(key);
        }
    }

    public void Destroy()
    {
        _root.Destroy();
        _clipMaterials.Clear();
    }

    private UIUnlitMaterial GetClippedUIUnlit(UIUnlitMaterial source, in Rect clipRect)
    {
        var key = new ClipMaterialKey(source, clipRect);
        if (!_clipMaterials.TryGetValue(key, out var entry) || entry.Material is not UIUnlitMaterial clone || clone.IsDestroyed)
        {
            var slot = _root.AddLocalSlot("ClipMaterial");
            clone = slot.AttachComponent<UIUnlitMaterial>();
            entry = new Entry(slot, clone);
            _clipMaterials[key] = entry;
        }

        entry.LastUsedFrame = _frame;
        CopyUIUnlit(source, clone, clipRect);
        return clone;
    }

    private UITextMaterial GetClippedUIText(UITextMaterial source, in Rect clipRect)
    {
        var key = new ClipMaterialKey(source, clipRect);
        if (!_clipMaterials.TryGetValue(key, out var entry) || entry.Material is not UITextMaterial clone || clone.IsDestroyed)
        {
            var slot = _root.AddLocalSlot("ClipTextMaterial");
            clone = slot.AttachComponent<UITextMaterial>();
            entry = new Entry(slot, clone);
            _clipMaterials[key] = entry;
        }

        entry.LastUsedFrame = _frame;
        CopyUIText(source, clone, clipRect);
        return clone;
    }

    private static void CopyUIUnlit(UIUnlitMaterial source, UIUnlitMaterial clone, in Rect clipRect)
    {
        bool changed = false;
        changed |= SetTarget(clone.Texture, source.Texture.Target);
        changed |= SetDirectTexture(clone, source.DirectTexture);
        changed |= Set(clone.TextureScale, source.TextureScale.Value);
        changed |= Set(clone.TextureOffset, source.TextureOffset.Value);
        changed |= Set(clone.TintColor, source.TintColor.Value);
        changed |= Set(clone.UseVertexColor, source.UseVertexColor.Value);
        changed |= Set(clone.AlphaClip, source.AlphaClip.Value);
        changed |= Set(clone.AlphaCutoff, source.AlphaCutoff.Value);
        changed |= Set(clone.BlendMode, source.BlendMode.Value);
        changed |= Set(clone.Culling, source.Culling.Value);
        changed |= Set(clone.ZWrite, source.ZWrite.Value);
        changed |= Set(clone.ZTest, source.ZTest.Value);
        changed |= Set(clone.RenderQueue, source.RenderQueue.Value);
        changed |= Set(clone.ColorMask, source.ColorMask.Value);
        changed |= Set(clone.StencilComparison, source.StencilComparison.Value);
        changed |= Set(clone.StencilOperation, source.StencilOperation.Value);
        changed |= Set(clone.StencilID, source.StencilID.Value);
        changed |= Set(clone.StencilWriteMask, source.StencilWriteMask.Value);
        changed |= Set(clone.StencilReadMask, source.StencilReadMask.Value);
        changed |= Set(clone.Rect, clipRect);
        changed |= Set(clone.RectClip, true);

        if (changed)
        {
            clone.ForceUpdate();
        }
    }

    private static void CopyUIText(UITextMaterial source, UITextMaterial clone, in Rect clipRect)
    {
        bool changed = false;
        changed |= SetTarget(clone.Texture, source.Texture.Target);
        changed |= SetDirectTexture(clone, source.DirectTexture);
        changed |= Set(clone.TextureScale, source.TextureScale.Value);
        changed |= Set(clone.TextureOffset, source.TextureOffset.Value);
        changed |= Set(clone.TintColor, source.TintColor.Value);
        changed |= Set(clone.UseVertexColor, source.UseVertexColor.Value);
        changed |= Set(clone.PixelRange, source.PixelRange.Value);
        changed |= Set(clone.AlphaClip, source.AlphaClip.Value);
        changed |= Set(clone.AlphaCutoff, source.AlphaCutoff.Value);
        changed |= Set(clone.BlendMode, source.BlendMode.Value);
        changed |= Set(clone.Culling, source.Culling.Value);
        changed |= Set(clone.ZWrite, source.ZWrite.Value);
        changed |= Set(clone.ZTest, source.ZTest.Value);
        changed |= Set(clone.RenderQueue, source.RenderQueue.Value);
        changed |= Set(clone.ColorMask, source.ColorMask.Value);
        changed |= Set(clone.Rect, clipRect);
        changed |= Set(clone.RectClip, true);

        if (changed)
        {
            clone.ForceUpdate();
        }
    }

    private static bool Set<T>(Sync<T> field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field.Value, value))
        {
            return false;
        }

        field.Value = value;
        return true;
    }

    private static bool SetTarget<T>(AssetRef<T> field, IAssetProvider<T>? target) where T : Asset
    {
        if (ReferenceEquals(field.Target, target))
        {
            return false;
        }

        field.Target = target;
        return true;
    }

    private static bool SetDirectTexture(UIUnlitMaterial clone, TextureAsset? texture)
    {
        if (ReferenceEquals(clone.DirectTexture, texture))
        {
            return false;
        }

        clone.DirectTexture = texture;
        return true;
    }

    private static bool SetDirectTexture(UITextMaterial clone, TextureAsset? texture)
    {
        if (ReferenceEquals(clone.DirectTexture, texture))
        {
            return false;
        }

        clone.DirectTexture = texture;
        return true;
    }

    private sealed class Entry
    {
        public readonly Slot Slot;
        public readonly IAssetProvider<MaterialAsset> Material;
        public int LastUsedFrame;

        public Entry(Slot slot, IAssetProvider<MaterialAsset> material)
        {
            Slot = slot;
            Material = material;
        }
    }

    private readonly struct ClipMaterialKey
    {
        private readonly IAssetProvider<MaterialAsset> _material;
        private readonly Rect _clipRect;

        public ClipMaterialKey(IAssetProvider<MaterialAsset> material, in Rect clipRect)
        {
            _material = material;
            _clipRect = clipRect;
        }

        public override bool Equals(object? obj)
        {
            return obj is ClipMaterialKey other
                && ReferenceEquals(_material, other._material)
                && _clipRect == other._clipRect;
        }

        public override int GetHashCode()
        {
            return System.HashCode.Combine(_material, _clipRect);
        }
    }
}
