// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public abstract class WidgetPreset : Component
{
    public readonly Sync<string> WidgetName;
    public readonly Sync<float2> MinSize;
    public readonly Sync<float2> PreferredSize;
    public readonly Sync<float2> MaxSize;

    protected WidgetPreset()
    {
        WidgetName = new Sync<string>(this, "Widget");
        MinSize = new Sync<float2>(this, new float2(120f, 80f));
        PreferredSize = new Sync<float2>(this, new float2(240f, 160f));
        MaxSize = new Sync<float2>(this, new float2(900f, 700f));
    }

    public Widget CreateWidget(Slot parent, string? name = null)
    {
        var slot = parent.AddSlot(name ?? WidgetName.Value);
        var widget = slot.AttachComponent<Widget>();
        widget.MinSize.Value = MinSize.Value;
        widget.PreferredSize.Value = PreferredSize.Value;
        widget.MaxSize.Value = MaxSize.Value;
        Build(widget, widget.CreateBuilder());
        return widget;
    }

    protected abstract void Build(Widget widget, UIBuilder builder);
}
