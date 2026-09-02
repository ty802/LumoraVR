// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;

namespace Helio.UI.Listing;

// One live row. Built ONCE per pooled instance and rebound as the list scrolls, so every handler a
// template wires must read this.Item at fire time - never close over the item it first saw. -xlinka
public abstract class ListingRow
{
    public ListingView View { get; internal set; } = null!;
    public Slot Slot { get; internal set; } = null!;
    public ListingItem? Item { get; internal set; }
    public bool Selected { get; internal set; }

    public abstract void Bind(ListingItem item);

    public virtual void OnSelectionChanged() { }

    // Drop anything that would otherwise keep the previous item alive while this row sits pooled.
    public virtual void Unbind() { }
}

public abstract class ListingRowTemplate
{
    public virtual float Height => 40f;

    public virtual float HeightFor(ListingItem item) => Height;

    // False leaves the row's pill transparent (section headers, separators). The view still attaches
    // the BorderedImage - a row has to own one graphic or its chunk bakes empty - it just never tints it.
    public virtual bool UsesRowBackground => true;

    // Runs once per pooled row instance, before Build. Default is the house pill row (label left,
    // controls right); override for a stacked or gridded row.
    public virtual void ConfigureRow(Slot row, ListingStyle style)
    {
        var layout = row.GetComponent<Layout.HorizontalLayout>() ?? row.AttachComponent<Layout.HorizontalLayout>();
        layout.Spacing.Value = 14f;
        layout.PaddingLeft.Value = 12f;
        layout.PaddingRight.Value = 12f;
        layout.ForceExpandWidth.Value = false;
        layout.ForceExpandHeight.Value = true;
    }

    // The row slot already carries its RectTransform, its GraphicChunkRoot and its background; fill
    // the contents. Anchoring is the view's business - a template writing the row rect would fight
    // the virtual placement.
    public abstract ListingRow Build(ListingView view, UIBuilder builder, Slot row);
}

// item type (and optional kind key) -> row template. Screens declare these once and the view stops
// caring what kinds of rows it is scrolling.
public sealed class ListingTemplateMapper
{
    private readonly Dictionary<Type, ListingRowTemplate> _byType = new();
    private readonly Dictionary<string, ListingRowTemplate> _byKind = new(StringComparer.Ordinal);
    private ListingRowTemplate? _fallback;

    public float DefaultHeight { get; set; } = 40f;

    public ListingTemplateMapper Map<T>(ListingRowTemplate template) where T : ListingItem
        => Map(typeof(T), template);

    public ListingTemplateMapper Map(Type itemType, ListingRowTemplate template)
    {
        _byType[itemType] = template;
        return this;
    }

    public ListingTemplateMapper MapKind(string kind, ListingRowTemplate template)
    {
        if (!string.IsNullOrEmpty(kind))
            _byKind[kind] = template;
        return this;
    }

    public ListingTemplateMapper Fallback(ListingRowTemplate template)
    {
        _fallback = template;
        return this;
    }

    // Kind wins over type so one item class can be routed at several looks. Then exact type, then the
    // base chain (a screen can map ListingItem once and catch everything it did not name), then the
    // fallback. Null means the view skips the item entirely rather than rendering a blank row. -xlinka
    public ListingRowTemplate? Resolve(ListingItem item)
    {
        if (item.Kind.Length > 0 && _byKind.TryGetValue(item.Kind, out var byKind))
            return byKind;

        for (var type = item.GetType(); type != null; type = type.BaseType)
        {
            if (_byType.TryGetValue(type, out var byType))
                return byType;
            if (type == typeof(ListingItem))
                break;
        }

        return _fallback;
    }

    public float HeightFor(ListingItem item) => Resolve(item)?.HeightFor(item) ?? DefaultHeight;
}
