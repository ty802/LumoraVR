// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.
using Helio.UI;

namespace Lumora.Core.Components;

[ComponentCategory("Hidden")]
public sealed class InspectorNoteEditor : Component
{
    public readonly SyncRef<Component> Target = new();
    public readonly SyncRef<TextInput> Input = new();
    private const float RefreshInterval = 0.25f;
    private float _refresh;

    // The note component only exists once someone writes one, so an untouched component carries nothing.
    [SyncMethod]
    public void OnTyped(TextInput input, string text)
    {
        var target = Target.Target;
        if (target == null || target.IsDestroyed)
            return;
        var note = InspectorComponentNote.Find(target);
        if (note == null && string.IsNullOrEmpty(text))
            return;
        if (note == null)
        {
            note = target.Slot.AttachComponent<InspectorComponentNote>();
            note.Target.Target = target;
        }
        if (note.Note.Value != text)
            note.Note.Value = text;
    }

    // Picks up an edit from another panel or peer, never under the caret of the person typing here.
    public override void OnUpdate(float delta)
    {
        if ((_refresh += delta) < RefreshInterval)
            return;
        _refresh = 0;
        if (Input.Target is not { IsFocused: false } input || Target.Target is not { IsDestroyed: false } target)
            return;
        string note = InspectorComponentNote.Find(target)?.Note.Value ?? "";
        if (input.Text.Value != note)
            input.Text.Value = note;
    }
}
