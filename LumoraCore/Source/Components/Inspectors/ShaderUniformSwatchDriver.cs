// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core.Components.Assets;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

// Keeps a swatch Image tinted from a shader uniform's color value. Self-contained so a color param row
// gets a live swatch wherever it is built (the focused material panel or a generic collection editor)
// without the host having to track and tear down the subscription. -xlinka
[ComponentCategory("Utility/Inspectors")]
public sealed class ShaderUniformSwatchDriver : Component
{
    public readonly SyncRef<Image> Swatch;
    public readonly SyncRef<ShaderUniformParam> Param;

    private Sync<float4>? _watched;
    private Action<float4>? _handler;

    public ShaderUniformSwatchDriver()
    {
        Swatch = new SyncRef<Image>(this);
        Param = new SyncRef<ShaderUniformParam>(this);
    }

    public override void OnStart()
    {
        base.OnStart();
        Hook();
        Refresh();
    }

    public override void OnDestroy()
    {
        Unhook();
        base.OnDestroy();
    }

    private void Hook()
    {
        var param = Param.Target;
        if (param == null)
            return;
        _watched = param.Value;
        _handler = _ => Refresh();
        _watched.OnChanged += _handler;
    }

    private void Unhook()
    {
        if (_watched != null && _handler != null)
            _watched.OnChanged -= _handler;
        _watched = null;
        _handler = null;
    }

    private void Refresh()
    {
        var swatch = Swatch.Target;
        var param = Param.Target;
        if (swatch == null || swatch.IsDestroyed || param == null)
            return;
        var v = param.Value.Value;
        swatch.Tint.Value = new color(v.x, v.y, v.z, 1f);
    }
}
