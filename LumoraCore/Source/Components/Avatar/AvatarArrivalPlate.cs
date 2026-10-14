// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;
using LumoraMeshes = Lumora.Core.Components.Meshes;

namespace Lumora.Core.Components.Avatar;

// The readout over a stand-in: what is happening on the left, the percentage on the right, the bar, and
// under it the avatar's name with how many of its parts are here and how much has come down.
//
// A name plate's sibling, built from the same theme: the plate tint and hairline, the dash accent on the
// bar, the dash type. It hangs under the person's name plate and turns to each viewer the way the name
// plate does. Every piece is geometry on the theme's one plate material; the bar fills by scale, so
// moving it re-meshes nothing, and the text is only re-set when what it says changes. -xlinka
internal sealed class AvatarArrivalPlate
{
    private const float Width = 0.34f;
    private const float Height = 0.092f;
    private const float Inset = 0.018f;
    private const float BarWidth = Width - 2f * Inset;
    private const float BarHeight = 0.0065f;
    private const float PanelDepth = -0.004f;
    private const float TrackDepth = -0.002f;
    private const float FillDepth = -0.001f;

    // Past this the words are a smudge; the figure still says it is loading.
    private const float ViewDistance = 16f;

    private readonly Slot _fill;
    private readonly LumoraMeshes.RoundedQuadMesh _fillMesh;
    private readonly TextRenderer _state;
    private readonly TextRenderer _percent;
    private readonly TextRenderer _detail;
    private readonly RenderLayerOverride _band;
    private bool _failedLook;
    private float _barShown = -1f;

    public Slot Root { get; }

    public AvatarArrivalPlate(Slot parent, NameplateTheme? theme)
    {
        Root = parent.AddSlot("Readout");
        Root.AttachComponent<FaceLocalUser>();
        _band = Root.AttachComponent<RenderLayerOverride>();
        _band.Layer.Value = RenderLayerOverride.NameplateLayer;

        var material = theme?.PlateMaterial;
        Quad(Root, "Panel", 0f, PanelDepth, new float2(Width, Height), Height * 0.24f, NameplateTheme.PlateFill, material);
        var edge = Root.AddSlot("Edge");
        edge.LocalPosition.Value = new float3(0f, 0f, PanelDepth);
        var ring = edge.AttachComponent<LumoraMeshes.RoundedQuadRingMesh>();
        ring.Size.Value = new float2(Width, Height);
        ring.CornerRadius.Value = Height * 0.24f;
        ring.RimWidth.Value = 0.0016f;
        ring.Color = NameplateTheme.PlateEdge;
        Attach(edge, ring, material);

        var track = Quad(Root, "Track", -0.003f, TrackDepth, new float2(BarWidth, BarHeight), BarHeight * 0.5f,
            new color(DashTheme.SurfaceHover.r, DashTheme.SurfaceHover.g, DashTheme.SurfaceHover.b, 0.9f), material);
        _fill = Quad(track, "Fill", 0f, FillDepth - TrackDepth, new float2(BarWidth, BarHeight), BarHeight * 0.5f,
            DashTheme.Accent, material);
        _fillMesh = _fill.GetComponent<LumoraMeshes.RoundedQuadMesh>()!;

        float left = -Width * 0.5f + Inset;
        _state = Text("State", left, 0.022f, 0.026f, DashTheme.AccentHover, TextHorizontalAlignment.Left, theme);
        _percent = Text("Percent", -left, 0.022f, 0.026f, DashTheme.Text, TextHorizontalAlignment.Right, theme);
        _detail = Text("Detail", left, -0.026f, 0.0175f, DashTheme.TextDim, TextHorizontalAlignment.Left, theme);
    }

    private static Slot Quad(Slot parent, string name, float y, float z, float2 size, float radius, color fill,
        Lumora.Core.Assets.UnlitMaterial? material)
    {
        var slot = parent.AddSlot(name);
        slot.LocalPosition.Value = new float3(0f, y, z);
        var mesh = slot.AttachComponent<LumoraMeshes.RoundedQuadMesh>();
        mesh.Size.Value = size;
        mesh.CornerRadius.Value = radius;
        mesh.Color = fill;
        Attach(slot, mesh, material);
        return slot;
    }

    private static void Attach(Slot slot, LumoraMeshes.ProceduralMesh mesh, Lumora.Core.Assets.UnlitMaterial? material)
    {
        var renderer = slot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        if (material != null)
            renderer.Material.Target = material;
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;
        renderer.MaxViewDistance = ViewDistance;
        renderer.ViewDistanceFadeMargin = 2f;
    }

    private TextRenderer Text(string name, float x, float y, float size, color ink, TextHorizontalAlignment align,
        NameplateTheme? theme)
    {
        var slot = Root.AddSlot(name);
        slot.LocalPosition.Value = new float3(x, y, 0f);
        var label = slot.AttachComponent<TextRenderer>();
        if (theme?.Font != null)
            label.Font.Target = theme.Font;
        label.Size.Value = size;
        label.Color.Value = ink;
        label.HorizontalAlign.Value = align;
        label.VerticalAlign.Value = TextVerticalAlignment.Middle;
        label.OutlineThickness.Value = 0f;
        label.MaxViewDistance.Value = ViewDistance;
        label.Text.Value = string.Empty;
        return label;
    }

    // Your own readout hangs over your own head: mirrors and cameras, never your own eyes.
    public void SetOwnView(bool ownEyes)
    {
        int band = ownEyes ? RenderLayerOverride.OwnNameplateLayer : RenderLayerOverride.NameplateLayer;
        if (!_band.IsDestroyed && _band.Layer.Value != band)
            _band.Layer.Value = band;
    }

    public void SetText(string state, string percent, string detail, bool failed)
    {
        Set(_state, state);
        Set(_percent, percent);
        Set(_detail, detail);
        FitLabel(_detail, BarWidth);
        FitLabel(_state, BarWidth * 0.72f);
        if (failed == _failedLook)
            return;
        _failedLook = failed;
        _state.Color.Value = failed ? DashTheme.Warning : DashTheme.AccentHover;
        _fillMesh.Color = failed ? DashTheme.Warning : DashTheme.Accent;
    }

    private static void Set(TextRenderer label, string text)
    {
        if (!label.IsDestroyed && label.Text.Value != text)
            label.Text.Value = text;
    }

    // A long avatar name or a translated line shrinks to fit rather than running off the plate.
    private static void FitLabel(TextRenderer label, float room)
    {
        if (label.IsDestroyed)
            return;
        float width = label.RenderedSize.x;
        float scale = width > room ? room / width : 1f;
        var current = label.Slot.LocalScale.Value;
        if (System.MathF.Abs(current.x - scale) > 0.001f)
            label.Slot.LocalScale.Value = new float3(scale, scale, 1f);
    }

    public void SetBar(float fraction)
    {
        float width = System.Math.Clamp(fraction, 0.002f, 1f);
        if (System.MathF.Abs(width - _barShown) < 0.0005f)
            return;
        _barShown = width;
        _fill.LocalScale.Value = new float3(width, 1f, 1f);
        _fill.LocalPosition.Value = new float3(-BarWidth * 0.5f * (1f - width), 0f, FillDepth - TrackDepth);
    }

    public void Place(float3 position)
    {
        var parent = Root.Parent;
        if (parent == null)
            return;
        var local = parent.GlobalPointToLocal(position);
        if ((Root.LocalPosition.Value - local).LengthSquared > 1e-8f)
            Root.LocalPosition.Value = local;
    }
}
