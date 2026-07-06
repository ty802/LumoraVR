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
    private readonly Dictionary<PriorityMaterialKey, Entry> _priorityMaterials = new();
    private readonly Dictionary<StencilMaterialKey, Entry> _stencilMaterials = new();
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
            foreach (var entry in _priorityMaterials.Values)
            {
                entry.LastUsedFrame = 0;
            }
            foreach (var entry in _stencilMaterials.Values)
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

    public IAssetProvider<MaterialAsset>? GetRenderPriorityMaterial(IAssetProvider<MaterialAsset>? source, int renderPriority)
    {
        if (source == null || source.IsDestroyed)
        {
            return null;
        }

        return source switch
        {
            UIUnlitMaterial unlit => GetPriorityUIUnlit(unlit, renderPriority),
            UITextMaterial text => GetPriorityUIText(text, renderPriority),
            _ => source,
        };
    }

    // Clone of a UI material that swaps to the stencil-write or stencil-test shader variant. Write = a mask
    // shape stamping the stencil; Test = content clipped to where the stencil matches. The inherited rect
    // clip is folded in as an orthogonal AABB bound. Only UIUnlitMaterial sources (Image graphics, incl. the
    // mask shape) get a variant; text falls back to rect-clip so it stays within the mask's AABB. -xlinka
    public IAssetProvider<MaterialAsset>? GetStencilMaterial(IAssetProvider<MaterialAsset>? source, StencilRole role, Rect? clipRect)
    {
        if (source == null || source.IsDestroyed || role == StencilRole.None)
        {
            return source;
        }

        if (source is UIUnlitMaterial unlit)
        {
            return GetStencilUIUnlit(unlit, role, clipRect);
        }

        // Text content under a stencil mask: shape-clip it too (Test only; masks are Image shapes, never text).
        if (source is UITextMaterial text && role == StencilRole.Test)
        {
            return GetStencilUIText(text, clipRect);
        }

        return clipRect.HasValue ? GetClippedMaterial(source, clipRect.Value) : source;
    }

    private UITextMaterial GetStencilUIText(UITextMaterial source, Rect? clipRect)
    {
        var key = new StencilMaterialKey(source, StencilRole.Test, clipRect);
        if (!_stencilMaterials.TryGetValue(key, out var entry) || entry.Material is not UITextMaterial clone || clone.IsDestroyed)
        {
            var slot = _root.AddLocalSlot("StencilTestTextMaterial");
            clone = slot.AttachComponent<UIStencilTestTextMaterial>();
            entry = new Entry(slot, clone);
            _stencilMaterials[key] = entry;
        }

        entry.LastUsedFrame = _frame;
        CopyUIText(source, clone, clipRect, null);
        return clone;
    }

    private UIUnlitMaterial GetStencilUIUnlit(UIUnlitMaterial source, StencilRole role, Rect? clipRect)
    {
        var key = new StencilMaterialKey(source, role, clipRect);
        if (!_stencilMaterials.TryGetValue(key, out var entry) || entry.Material is not UIUnlitMaterial clone || clone.IsDestroyed)
        {
            var slot = _root.AddLocalSlot(role == StencilRole.Write ? "StencilWriteMaterial" : "StencilTestMaterial");
            clone = role == StencilRole.Write
                ? slot.AttachComponent<UIStencilWriteMaterial>()
                : slot.AttachComponent<UIStencilTestMaterial>();
            entry = new Entry(slot, clone);
            _stencilMaterials[key] = entry;
        }

        entry.LastUsedFrame = _frame;
        CopyUIUnlit(source, clone, clipRect, null);
        return clone;
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
            RemoveUnusedPriorityMaterials();
            RemoveUnusedStencilMaterials();
            return;
        }

        foreach (var key in remove)
        {
            var entry = _clipMaterials[key];
            entry.Slot.Destroy();
            _clipMaterials.Remove(key);
        }

        RemoveUnusedPriorityMaterials();
        RemoveUnusedStencilMaterials();
    }

    public void Destroy()
    {
        _root.Destroy();
        _clipMaterials.Clear();
        _priorityMaterials.Clear();
        _stencilMaterials.Clear();
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

    private UIUnlitMaterial GetPriorityUIUnlit(UIUnlitMaterial source, int renderPriority)
    {
        var key = new PriorityMaterialKey(source, renderPriority);
        if (!_priorityMaterials.TryGetValue(key, out var entry) || entry.Material is not UIUnlitMaterial clone || clone.IsDestroyed)
        {
            var slot = _root.AddLocalSlot("RenderPriorityMaterial");
            // Preserve the concrete material TYPE. The stencil variants (UIStencilWriteMaterial /
            // UIStencilTestMaterial) pick their shader via a per-subclass MaterialType, so cloning into a
            // plain UIUnlitMaterial here would silently strip the stencil shader - the per-surface priority
            // re-clone runs on EVERY material (AssignMaterials), so this is what made stencil masking inert
            // in the default banded path. -xlinka
            clone = AttachUIUnlitLike(slot, source);
            entry = new Entry(slot, clone);
            _priorityMaterials[key] = entry;
        }

        entry.LastUsedFrame = _frame;
        CopyUIUnlit(source, clone, null, RenderQueueForGodotPriority(renderPriority));
        // CopyUIUnlit copies base Sync fields only; carry the write variant's mask-visibility too.
        if (source is UIStencilWriteMaterial sourceWrite && clone is UIStencilWriteMaterial cloneWrite)
        {
            cloneWrite.ShowMaskGraphic.Value = sourceWrite.ShowMaskGraphic.Value;
        }
        return clone;
    }

    // Attach a UI material matching the source's concrete type so clones keep the stencil shader variant. -xlinka
    private static UIUnlitMaterial AttachUIUnlitLike(Slot slot, UIUnlitMaterial source)
    {
        return source switch
        {
            UIStencilWriteMaterial => slot.AttachComponent<UIStencilWriteMaterial>(),
            UIStencilTestMaterial => slot.AttachComponent<UIStencilTestMaterial>(),
            _ => slot.AttachComponent<UIUnlitMaterial>(),
        };
    }

    private static UITextMaterial AttachUITextLike(Slot slot, UITextMaterial source)
    {
        return source switch
        {
            UIStencilTestTextMaterial => slot.AttachComponent<UIStencilTestTextMaterial>(),
            _ => slot.AttachComponent<UITextMaterial>(),
        };
    }

    private UITextMaterial GetPriorityUIText(UITextMaterial source, int renderPriority)
    {
        var key = new PriorityMaterialKey(source, renderPriority);
        if (!_priorityMaterials.TryGetValue(key, out var entry) || entry.Material is not UITextMaterial clone || clone.IsDestroyed)
        {
            var slot = _root.AddLocalSlot("RenderPriorityTextMaterial");
            // Preserve the concrete type so a stencil-test text clone keeps the UI_TextStencil shader. -xlinka
            clone = AttachUITextLike(slot, source);
            entry = new Entry(slot, clone);
            _priorityMaterials[key] = entry;
        }

        entry.LastUsedFrame = _frame;
        CopyUIText(source, clone, null, RenderQueueForGodotPriority(renderPriority));
        return clone;
    }

    private static void CopyUIUnlit(UIUnlitMaterial source, UIUnlitMaterial clone, in Rect clipRect)
        => CopyUIUnlit(source, clone, clipRect, null);

    private static void CopyUIUnlit(UIUnlitMaterial source, UIUnlitMaterial clone, Rect? clipRect, int? renderQueueOverride)
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
        changed |= Set(clone.RenderQueue, renderQueueOverride ?? source.RenderQueue.Value);
        changed |= Set(clone.ColorMask, source.ColorMask.Value);
        changed |= Set(clone.StencilComparison, source.StencilComparison.Value);
        changed |= Set(clone.StencilOperation, source.StencilOperation.Value);
        changed |= Set(clone.StencilID, source.StencilID.Value);
        changed |= Set(clone.StencilWriteMask, source.StencilWriteMask.Value);
        changed |= Set(clone.StencilReadMask, source.StencilReadMask.Value);
        changed |= Set(clone.Rect, clipRect ?? source.Rect.Value);
        changed |= Set(clone.RectClip, clipRect.HasValue || source.RectClip.Value);
        changed |= Set(clone.ClipOffset, source.ClipOffset.Value);

        if (changed)
        {
            clone.ForceUpdate();
        }
    }

    private static void CopyUIText(UITextMaterial source, UITextMaterial clone, in Rect clipRect)
        => CopyUIText(source, clone, clipRect, null);

    private static void CopyUIText(UITextMaterial source, UITextMaterial clone, Rect? clipRect, int? renderQueueOverride)
    {
        bool changed = false;
        changed |= SetTarget(clone.Texture, source.Texture.Target);
        changed |= SetDirectTexture(clone, source.DirectTexture);
        changed |= Set(clone.TextureScale, source.TextureScale.Value);
        changed |= Set(clone.TextureOffset, source.TextureOffset.Value);
        changed |= Set(clone.TintColor, source.TintColor.Value);
        changed |= Set(clone.UseVertexColor, source.UseVertexColor.Value);
        changed |= Set(clone.PixelRange, source.PixelRange.Value);
        changed |= Set(clone.UseMSDF, source.UseMSDF.Value);
        changed |= Set(clone.AlphaClip, source.AlphaClip.Value);
        changed |= Set(clone.AlphaCutoff, source.AlphaCutoff.Value);
        changed |= Set(clone.BlendMode, source.BlendMode.Value);
        changed |= Set(clone.Culling, source.Culling.Value);
        changed |= Set(clone.ZWrite, source.ZWrite.Value);
        changed |= Set(clone.ZTest, source.ZTest.Value);
        changed |= Set(clone.RenderQueue, renderQueueOverride ?? source.RenderQueue.Value);
        changed |= Set(clone.ColorMask, source.ColorMask.Value);
        changed |= Set(clone.StencilComparison, source.StencilComparison.Value);
        changed |= Set(clone.StencilOperation, source.StencilOperation.Value);
        changed |= Set(clone.StencilID, source.StencilID.Value);
        changed |= Set(clone.StencilWriteMask, source.StencilWriteMask.Value);
        changed |= Set(clone.StencilReadMask, source.StencilReadMask.Value);
        changed |= Set(clone.Rect, clipRect ?? source.Rect.Value);
        changed |= Set(clone.RectClip, clipRect.HasValue || source.RectClip.Value);
        changed |= Set(clone.ClipOffset, source.ClipOffset.Value);

        if (changed)
        {
            clone.ForceUpdate();
        }
    }

    // Set the clip_offset shader param on every live clone (clip + priority + stencil). Used by render-offset
    // scrolling: the clip rect stays fixed in canvas space and this pins the clip window to the viewport as
    // the content chunk slides, so a scroll is a per-frame uniform write - no re-mesh, no re-clone. -xlinka
    public void SetClipOffset(float2 offset, bool persist)
    {
        foreach (var e in _clipMaterials.Values) SetClipOffsetOn(e.Material, offset, persist);
        foreach (var e in _priorityMaterials.Values) SetClipOffsetOn(e.Material, offset, persist);
        foreach (var e in _stencilMaterials.Values) SetClipOffsetOn(e.Material, offset, persist);
    }

    private static void SetClipOffsetOn(IAssetProvider<MaterialAsset> material, float2 offset, bool persist)
    {
        // Live scroll pushes clip_offset STRAIGHT to the generated material's shader param - one
        // SetShaderParameter. The old path set the Sync + ForceUpdate, which re-ran the whole UpdateMaterial
        // (every param) and queued an asset change PER material EVERY frame; with a screen of cards that
        // O(materials) rebuild is the scroll lag. -xlinka
        if (persist)
        {
            // Rebuild path (infrequent): also write the Sync so the material's own UpdateMaterial - which runs on
            // a structural rebuild and would otherwise re-push the baked 0 and snap the content back to the top -
            // carries the current offset. -xlinka
            switch (material)
            {
                case UIUnlitMaterial u: u.ClipOffset.Value = offset; break;
                case UITextMaterial t: t.ClipOffset.Value = offset; break;
            }
        }
        // ApplyFloat2Now, not SetFloat2: SetFloat2 only STAGES the value (flushed later by a full ApplyChanges
        // that re-pushes every property). On the live scroll path there is no ApplyChanges, so a plain SetFloat2
        // never reaches the shader and the content stops moving. This flushes just clip_offset. -xlinka
        material.Asset?.ApplyFloat2Now("ClipOffset", offset);
    }

    private void RemoveUnusedPriorityMaterials()
    {
        List<PriorityMaterialKey>? remove = null;
        foreach (var pair in _priorityMaterials)
        {
            if (pair.Value.LastUsedFrame == _frame)
            {
                continue;
            }

            remove ??= new List<PriorityMaterialKey>();
            remove.Add(pair.Key);
        }

        if (remove == null)
        {
            return;
        }

        foreach (var key in remove)
        {
            var entry = _priorityMaterials[key];
            entry.Slot.Destroy();
            _priorityMaterials.Remove(key);
        }
    }

    private void RemoveUnusedStencilMaterials()
    {
        List<StencilMaterialKey>? remove = null;
        foreach (var pair in _stencilMaterials)
        {
            if (pair.Value.LastUsedFrame == _frame)
            {
                continue;
            }

            remove ??= new List<StencilMaterialKey>();
            remove.Add(pair.Key);
        }

        if (remove == null)
        {
            return;
        }

        foreach (var key in remove)
        {
            var entry = _stencilMaterials[key];
            entry.Slot.Destroy();
            _stencilMaterials.Remove(key);
        }
    }

    private static int RenderQueueForGodotPriority(int renderPriority)
    {
        return 3000 + System.Math.Clamp(renderPriority, -128, 127);
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

        field.Target = target!;
        return true;
    }

    private static bool SetDirectTexture(UIUnlitMaterial clone, TextureAsset? texture)
    {
        if (ReferenceEquals(clone.DirectTexture, texture))
        {
            return false;
        }

        clone.DirectTexture = texture!;
        return true;
    }

    private static bool SetDirectTexture(UITextMaterial clone, TextureAsset? texture)
    {
        if (ReferenceEquals(clone.DirectTexture, texture))
        {
            return false;
        }

        clone.DirectTexture = texture!;
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

    private readonly struct PriorityMaterialKey
    {
        private readonly IAssetProvider<MaterialAsset> _material;
        private readonly int _renderPriority;

        public PriorityMaterialKey(IAssetProvider<MaterialAsset> material, int renderPriority)
        {
            _material = material;
            _renderPriority = renderPriority;
        }

        public override bool Equals(object? obj)
        {
            return obj is PriorityMaterialKey other
                && ReferenceEquals(_material, other._material)
                && _renderPriority == other._renderPriority;
        }

        public override int GetHashCode()
        {
            return System.HashCode.Combine(_material, _renderPriority);
        }
    }

    private readonly struct StencilMaterialKey
    {
        private readonly IAssetProvider<MaterialAsset> _material;
        private readonly StencilRole _role;
        private readonly bool _hasClip;
        private readonly Rect _clipRect;

        public StencilMaterialKey(IAssetProvider<MaterialAsset> material, StencilRole role, Rect? clipRect)
        {
            _material = material;
            _role = role;
            _hasClip = clipRect.HasValue;
            _clipRect = clipRect ?? default;
        }

        public override bool Equals(object? obj)
        {
            return obj is StencilMaterialKey other
                && ReferenceEquals(_material, other._material)
                && _role == other._role
                && _hasClip == other._hasClip
                && _clipRect == other._clipRect;
        }

        public override int GetHashCode()
        {
            return System.HashCode.Combine(_material, _role, _hasClip, _clipRect);
        }
    }
}
