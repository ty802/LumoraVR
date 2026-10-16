// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

// live preview: press it while holding a texture card (or grip-drop one onto it) to PUT a texture
// in, the ≡ button spawns a card to PULL the current one back out. same drop/assign plumbing as
// RefMemberEditor, tuned for texture asset refs.
[ComponentCategory("Utility/Inspectors")]
public class TextureRefMemberEditor : MemberEditor, IProxyReceiver, IProxySource
{
    private readonly SyncRef<RawImage> _preview;
    private readonly SyncRef<Text> _targetText;
    private readonly SyncRef<IField<color>> _frameTint;

    public TextureRefMemberEditor()
    {
        _preview = new SyncRef<RawImage>(this);
        _targetText = new SyncRef<Text>(this);
        _infoText = new SyncRef<Text>(this);
        _frameTint = new SyncRef<IField<color>>(this);
    }

    private ISyncRef? Ref => TargetMember.Target as ISyncRef;

    private readonly SyncRef<Text> _infoText;

    protected override void BuildUI(UIBuilder ui)
    {
        // The canonical texture block: a 96px square checkerboard preview (the checker reads alpha and
        // "empty" honestly), an info column (what it is / its dimensions), then the action buttons. The
        // preview square is the assign button - press it while holding a texture card and it drops in. -xlinka
        ui.PushStyle();
        ui.MinWidth(92f);
        ui.PreferredWidth(92f);
        ui.FlexibleWidth(0f);
        var previewSlot = ui.Next("Preview");
        var frame = previewSlot.AttachComponent<BorderedImage>();
        frame.Tint.Value = PreviewFrameColor;
        frame.BorderTint.Value = new color(0.45f, 0.38f, 0.80f, 0.6f);
        frame.BorderThickness.Value = 1.5f;
        // The preview frame is this row's field-state signal (driven magenta, linked cyan, broken
        // gray) - the job the readout button does on a plain reference row. -xlinka
        _frameTint.Target = frame.Tint;
        BindStateTint(frame.Tint, PreviewFrameColor);
        previewSlot.AttachComponent<Button>().SetAction(OnPreviewPressed);

        var checker = previewSlot.AttachComponent<Lumora.Core.Assets.CheckerTextureProvider>();
        checker.ColorA.Value = new color(0.20f, 0.20f, 0.24f, 1f);
        checker.ColorB.Value = new color(0.12f, 0.12f, 0.15f, 1f);
        checker.CellSize.Value = 8;
        var checkerSlot = previewSlot.AddSlot("Checker");
        var checkerRect = checkerSlot.AttachComponent<RectTransform>();
        checkerRect.AnchorMin.Value = float2.Zero;
        checkerRect.AnchorMax.Value = float2.One;
        checkerRect.OffsetMin.Value = new float2(2f, 2f);
        checkerRect.OffsetMax.Value = new float2(-2f, -2f);
        var checkerImage = checkerSlot.AttachComponent<RawImage>();
        checkerImage.Texture.Target = checker;

        var thumb = previewSlot.AddSlot("Thumb");
        var thumbRect = thumb.AttachComponent<RectTransform>();
        thumbRect.AnchorMin.Value = float2.Zero;
        thumbRect.AnchorMax.Value = float2.One;
        thumbRect.OffsetMin.Value = new float2(2f, 2f);
        thumbRect.OffsetMax.Value = new float2(-2f, -2f);
        _preview.Target = thumb.AttachComponent<RawImage>();
        ui.PopStyle();

        // Info column: line 1 = what the texture is, line 2 = dimensions (muted).
        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var infoSlot = ui.Next("Info");
        var infoLayout = infoSlot.AttachComponent<Helio.UI.Layout.VerticalLayout>();
        infoLayout.Spacing.Value = 2f;
        infoLayout.PaddingLeft.Value = 6f;
        infoLayout.ForceExpandWidth.Value = true;
        infoLayout.ForceExpandHeight.Value = false;
        var infoUi = new UIBuilder(infoSlot);
        InspectorUI.ApplyTheme(infoUi, Slot);

        infoUi.PushStyle();
        infoUi.MinHeight(24f);
        _targetText.Target = infoUi.Text("null", InspectorUI.FontSize, InspectorUI.MutedColor);
        InspectorUI.FillParent(_targetText.Target.RectTransform!);
        _targetText.Target.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        _targetText.Target.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        _targetText.Target.RichText.Value = true;
        infoUi.PopStyle();

        infoUi.PushStyle();
        infoUi.MinHeight(20f);
        _infoText.Target = infoUi.Text("---", InspectorUI.FontSize - 1f, InspectorUI.MutedColor);
        InspectorUI.FillParent(_infoText.Target.RectTransform!);
        _infoText.Target.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        _infoText.Target.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        infoUi.PopStyle();
        ui.PopStyle();

        // Pull the current texture back out as a grabbable card.
        ui.PushStyle();
        ui.MinWidth(28f);
        ui.PreferredWidth(28f);
        ui.FlexibleWidth(0f);
        ui.TextColor(InspectorUI.AccentColor);
        ui.Button("≡", OnPullPressed);
        ui.PopStyle();

        ui.PushStyle();
        ui.MinWidth(28f);
        ui.PreferredWidth(28f);
        ui.FlexibleWidth(0f);
        ui.TextColor(InspectorUI.DangerColor);
        ui.Button("Ø", OnClearPressed);
        ui.PopStyle();
    }

    // preview frame backing while the member is in its ordinary, undriven state
    private static readonly color PreviewFrameColor = new color(0.03f, 0.03f, 0.06f, 1f);

    protected override void RefreshDisplay()
    {
        var provider = Ref?.Target as IAssetProvider<TextureAsset>;

        // Steady-state recolors belong to the row's tint component; this covers a freshly built row,
        // where a drive can already be attached before the tint's first update runs.
        var frameTint = _frameTint.Target;
        if (frameTint != null && !frameTint.IsDestroyed)
            InspectorUI.ApplyStateTint(frameTint, FieldStateColor(PreviewFrameColor));

        var preview = _preview.Target;
        if (preview != null && !preview.IsDestroyed)
        {
            // Show the texture; when empty, disable the RawImage so its default-white sample doesn't render as a
            // blank white square - the checkerboard reads through as an "empty" slot instead. -xlinka
            // Null is intentional here (clears the preview to the checkerboard); Target's setter already
            // handles a null assignment the same way SyncRef.Clear() does. Target is typed non-nullable
            // since it's shared by every non-nullable-target usage across the codebase. -xlinka
            preview.Texture.Target = provider!;
            preview.Enabled.Value = provider != null;
        }

        var text = _targetText.Target;
        var info = _infoText.Target;
        if (provider is Component component)
        {
            if (text != null && !text.IsDestroyed)
            {
                text.Content.Value = $"{component.GetType().Name} on {component.Slot?.SlotName.Value}";
                text.Color.Value = InspectorUI.TextColor;
            }
            if (info != null && !info.IsDestroyed)
            {
                var asset = provider.Asset;
                info.Content.Value = DescribeTexture(asset);
            }
        }
        else
        {
            if (text != null && !text.IsDestroyed)
            {
                text.Content.Value = "null";
                text.Color.Value = InspectorUI.MutedColor;
            }
            if (info != null && !info.IsDestroyed)
                info.Content.Value = "---";
        }
    }

    // The row's second line: dimensions plus what the texture actually IS. Format comes from the
    // asset's measured metadata (content layout, and the GPU format once the renderer reports one),
    // never from an assumption that everything is RGBA8 - a block-compressed variant and a font
    // atlas are not the same thing, and this line is where you notice. Procedural textures carry no
    // metadata, so they show dimensions alone rather than a made-up format. -xlinka
    private static string DescribeTexture(TextureAsset? asset)
    {
        if (asset == null || asset.Width <= 0)
            return "---";

        string size = $"{asset.Width}x{asset.Height}";
        var metadata = asset.Metadata;
        if (metadata == null)
            return size;

        string line = $"{size}  {metadata.DescribeFormat()}";
        if (metadata.MipCount > 1)
            line += $"  {metadata.MipCount} mips";
        if (asset.LoadedVariant is { IsOriginal: false } variant)
            line += $"  [{variant.MaxSize}px]";
        return line;
    }

    // nothing held = nothing; use ≡ to pull one out
    [SyncMethod]
    public void OnPreviewPressed(Button button, UIInteractionContext context)
    {
        HeldTexture.OfferHeldBy(context.Actor, this);
    }

    [SyncMethod]
    public void OnPullPressed(Button button, UIInteractionContext context)
    {
        var target = Ref?.Target;
        if (target == null || target.IsDestroyed || World == null)
            return;
        var head = World.LocalUser?.Root?.HeadSlot;
        float3 point = context.WorldPoint;
        float3 offset = head != null && (head.GlobalPosition - point).LengthSquared > 1e-6f
            ? (head.GlobalPosition - point).Normalized * 0.25f
            : float3.Up * 0.1f;
        ReferenceProxy.Spawn(World, target, point + offset);
    }

    // grip on the preview block hands over a card for the texture currently assigned
    public IGrabbable? TryCreateProxy(Grabber grabber, in float3 spawnPoint)
    {
        var target = Ref?.Target;
        if (target == null || target.IsDestroyed || World == null)
            return null;
        return ReferenceProxy.Spawn(World, target, spawnPoint);
    }

    [SyncMethod]
    public void OnClearPressed(Button button, UIInteractionContext context)
    {
        var reference = Ref;
        if (reference == null || IsReadOnly)
            return;
        object? before = Field?.BoxedValue;
        reference.Clear();
        if (Field is { } field)
            InspectorUndo.RecordEdit(this, field, before, field.BoxedValue);
        RefreshDisplay();
    }

    // grip released, or primary pressed, over the preview while holding a texture's card or a textured
    // object: assign the texture the hand means, by the same rule a shader card's texture slot goes by
    public bool TryReceiveProxy(IReadOnlyList<IGrabbable> held, Grabber grabber)
    {
        var reference = Ref;
        // A driven reference is owned by its drive; dropping onto it would be silently reverted.
        if (reference == null || IsReadOnly)
            return false;

        return HeldTexture.Offer(held, grabber, texture =>
        {
            object? before = Field?.BoxedValue;
            if (!TryAssign(reference, texture))
                return false;

            if (Field is { } field && !Equals(before, field.BoxedValue))
                InspectorUndo.RecordEdit(this, field, before, field.BoxedValue);
            RefreshDisplay();
            return true;
        });
    }

    // The plain reference row's assignment, for its type check and its guard: a texture another world or
    // only this machine has fails here rather than throwing out of the release.
    private static bool TryAssign(ISyncRef reference, IWorldElement element)
        => RefMemberEditor.TryAssign(reference, element);
}
