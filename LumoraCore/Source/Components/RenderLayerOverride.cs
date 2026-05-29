// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

[ComponentCategory("Rendering")]
public class RenderLayerOverride : ImplementableComponent<IHook>
{
    public const int HiddenLayer = 1 << 3;

    public readonly Sync<int> Layer = new();

    public override void OnInit()
    {
        base.OnInit();
        Layer.Value = HiddenLayer;
    }
}
