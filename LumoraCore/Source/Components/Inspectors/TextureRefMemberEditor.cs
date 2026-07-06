// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

/// <summary>
/// Texture reference editor: a live preview thumbnail next to the usual ref controls, so texture
/// fields read like the classic inspector instead of an opaque name.
/// </summary>
public class TextureRefMemberEditor : MemberEditor
{
    private readonly SyncRef<RawImage> _preview;
    private readonly SyncRef<Text> _targetText;

    public TextureRefMemberEditor()
    {
        _preview = new SyncRef<RawImage>(this);
        _targetText = new SyncRef<Text>(this);
    }

    private ISyncRef? Ref => TargetMember.Target as ISyncRef;

    protected override void BuildUI(UIBuilder ui)
    {
        ui.PushStyle();
        ui.MinWidth(28f);
        ui.PreferredWidth(28f);
        ui.FlexibleWidth(0f);
        var previewSlot = ui.Next("Preview");
        var backdrop = previewSlot.AttachComponent<Image>();
        backdrop.Tint.Value = InspectorUI.RowColor;
        var previewChild = previewSlot.AddSlot("Thumb");
        InspectorUI.FillParent(previewChild.AttachComponent<RectTransform>());
        _preview.Target = previewChild.AttachComponent<RawImage>();
        ui.PopStyle();

        ui.PushStyle();
        ui.FlexibleWidth(1f);
        _targetText.Target = ui.Text("", InspectorUI.FontSize, InspectorUI.TextColor);
        InspectorUI.FillParent(_targetText.Target.RectTransform!);
        _targetText.Target.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ui.PopStyle();

        ui.PushStyle();
        ui.MinWidth(30f);
        ui.PreferredWidth(30f);
        ui.FlexibleWidth(0f);
        ui.Button("X", OnClearPressed);
        ui.PopStyle();
    }

    protected override void RefreshDisplay()
    {
        var reference = Ref;
        var provider = reference?.Target as IAssetProvider<TextureAsset>;

        var preview = _preview.Target;
        if (preview != null && !preview.IsDestroyed)
            preview.Texture.Target = provider!;

        var text = _targetText.Target;
        if (text != null && !text.IsDestroyed)
        {
            if (provider is Component component)
            {
                text.Content.Value = $"{component.GetType().Name} on {component.Slot?.SlotName.Value}";
                text.Color.Value = InspectorUI.TextColor;
            }
            else
            {
                text.Content.Value = "null";
                text.Color.Value = InspectorUI.MutedColor;
            }
        }
    }

    [SyncMethod]
    public void OnClearPressed(Button button, UIInteractionContext context)
    {
        var reference = Ref;
        if (reference == null)
            return;
        object? before = Field?.BoxedValue;
        reference.Clear();
        if (Field is { } field)
            InspectorUndo.RecordEdit(this, field, before, field.BoxedValue);
        RefreshDisplay();
    }
}
