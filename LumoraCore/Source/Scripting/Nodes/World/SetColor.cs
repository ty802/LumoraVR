// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Components;
using Lumora.Core.Math;
using Lumora.Script;
using HelioText = Helio.UI.Text;

namespace Lumora.Core.Scripting;

// Tints what a slot shows: its text renderer, else its UI text, else the main colour of the first
// material its mesh renderer draws with that has one. A material is shared by everything drawn with it,
// so tinting one tints them all.
[Node("World/Components/Set Color", Capability.WorldWrite)]
public sealed class SetColor : ActionNode
{
    public static void Run(ScriptContext ctx, Slot? slot, color color)
    {
        if (!ScriptReads.Visible(ctx, slot))
            return;
        if (slot!.GetComponent<TextRenderer>() is { IsDestroyed: false } renderer)
        {
            ScriptWrites.Write(ctx, renderer.Color, color);
            return;
        }
        if (slot.GetComponent<HelioText>() is { IsDestroyed: false } label)
        {
            ScriptWrites.Write(ctx, label.Color, color);
            return;
        }
        if (slot.GetComponent<MeshRenderer>() is not { IsDestroyed: false } mesh)
            return;
        for (int i = 0; i < mesh.Materials.Count; i++)
        {
            if (MainColor(mesh.Materials[i]) is { } main)
            {
                ScriptWrites.Write(ctx, main, (colorHDR)color);
                return;
            }
        }
    }

    private static Sync<colorHDR>? MainColor(IAssetProvider<MaterialAsset>? material) => material switch
    {
        UnlitMaterial { IsDestroyed: false } unlit => unlit.TintColor,
        PBS_Metallic { IsDestroyed: false } metallic => metallic.AlbedoColor,
        PBS_Specular { IsDestroyed: false } specular => specular.AlbedoColor,
        XiexeToonMaterial { IsDestroyed: false } toon => toon.AlbedoColor,
        _ => null,
    };
}
