// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

/// <summary>
/// Editor for SyncRef members: target readout, clear, and "set from the inspector's selected slot"
/// (tries the slot itself, then each of its components, so selecting a slot is enough to wire a
/// material/mesh/component reference). Grab-and-drop proxies come later; this covers wiring today.
/// </summary>
public class RefMemberEditor : MemberEditor
{
    private readonly SyncRef<Text> _targetText;

    public RefMemberEditor()
    {
        _targetText = new SyncRef<Text>(this);
    }

    private ISyncRef? Ref => TargetMember.Target as ISyncRef;

    protected override void BuildUI(UIBuilder ui)
    {
        ui.PushStyle();
        ui.FlexibleWidth(1f);
        _targetText.Target = ui.Text("", InspectorUI.FontSize, InspectorUI.TextColor);
        InspectorUI.FillParent(_targetText.Target.RectTransform!);
        _targetText.Target.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ui.PopStyle();

        ui.PushStyle();
        ui.MinWidth(44f);
        ui.PreferredWidth(44f);
        ui.FlexibleWidth(0f);
        ui.Button("Set", OnSetPressed);
        ui.Button("X", OnClearPressed);
        ui.PopStyle();
    }

    protected override void RefreshDisplay()
    {
        var text = _targetText.Target;
        if (text == null || text.IsDestroyed)
            return;

        var reference = Ref;
        var target = reference?.Target;
        if (target == null)
        {
            text.Content.Value = "null";
            text.Color.Value = InspectorUI.MutedColor;
            return;
        }

        string label = target switch
        {
            Slot slot => $"{slot.SlotName.Value} (slot)",
            Component component => $"{component.GetType().Name} on {component.Slot?.SlotName.Value}",
            ISyncMember member => $"{member.Name} ({target.GetType().Name})",
            _ => target.GetType().Name
        };
        text.Content.Value = label;
        text.Color.Value = InspectorUI.TextColor;
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

    [SyncMethod]
    public void OnSetPressed(Button button, UIInteractionContext context)
    {
        var reference = Ref;
        var panel = Slot.GetComponentInParent<SceneInspectorPanel>();
        var selected = panel?.Selected.Target;
        if (reference == null || selected == null)
            return;

        object? before = Field?.BoxedValue;
        // The slot itself first, then anything on it that fits the reference's target type.
        if (!reference.TrySet(selected))
        {
            foreach (var component in selected.GetAllComponents())
            {
                if (reference.TrySet(component))
                    break;
            }
        }
        if (Field is { } field && !Equals(before, field.BoxedValue))
            InspectorUndo.RecordEdit(this, field, before, field.BoxedValue);
        RefreshDisplay();
    }
}
