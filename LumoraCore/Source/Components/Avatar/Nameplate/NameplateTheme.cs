// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Import;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// Everything every nameplate in a world shares: one font, one material. Found or built once per world
// on a LOCAL slot, so twenty users in a room cost one set of these, not twenty.
//
// The material carries no colour and no texture of its own - tint is white, use_vertex_color is on,
// and every piece of the plate paints itself through its corner colours. Shape is GEOMETRY now
// (RoundedQuadMesh, RoundedQuadRingMesh), so the rounded-rect textures this used to carry are gone
// and panel, rim, chips and status dot all share the one material. That is what lets a chip change
// colour on a role change without allocating anything, and it keeps the whole plate on one draw
// material instead of two. -xlinka
[ComponentCategory("Users/Avatar")]
public sealed class NameplateTheme : Component
{
    public const string ThemeSlotName = "Nameplate Theme";

    private const string FallbackFontPath = "res://Assets/Fonts/FiraCode/FiraCode-SemiBold.ttf";

    public FontProvider? Font { get; private set; }
    public UnlitMaterial? PlateMaterial { get; private set; }

    public override void OnStart()
    {
        base.OnStart();
        Build();
    }

    private void Build()
    {
        if (Slot == null || Font != null)
            return;

        var fontSlot = Slot.AddSlot("Font");
        Font = fontSlot.AttachComponent<FontProvider>();
        // Text with no font renders NOTHING, so the plate has to bring its own. The dashboard registers a
        // URL at startup; a headless run or a template that spawns a user itself has none, hence the
        // fallback.
        var url = ImportDialog.ResolveFontUrl(World) ?? new Uri(FallbackFontPath);
        Font.URL.Value = url;
        Font.FallbackURLs.Add(url);

        var materialSlot = Slot.AddSlot("Plate");
        PlateMaterial = materialSlot.AttachComponent<UnlitMaterial>();
        PlateMaterial.TintColor.Value = colorHDR.White;
        PlateMaterial.UseVertexColor.Value = true;
        PlateMaterial.BlendMode.Value = BlendMode.Alpha;
        // The plate is a flat billboard with no back to hide, and a viewer standing behind somebody
        // still gets a readable plate out of it.
        PlateMaterial.Culling.Value = Culling.None;
    }

    // Find-or-build, on a local slot under the world root. Every peer builds its own copy and none of it
    // replicates or saves.
    public static NameplateTheme? For(World? world)
    {
        var root = world?.RootSlot;
        if (root == null || root.IsDestroyed)
            return null;

        var slot = root.FindChild(ThemeSlotName, recursive: false);
        var theme = slot?.GetComponent<NameplateTheme>();
        if (theme != null && !theme.IsDestroyed)
            return theme;

        slot?.Destroy();
        slot = root.AddLocalSlot(ThemeSlotName);
        slot.Persistent.Value = false;
        theme = slot.AttachComponent<NameplateTheme>();
        // OnStart is deferred a tick and the first plate wants the font in the same breath it is built.
        theme.Build();
        return theme;
    }
}
