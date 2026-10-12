// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.CompilerServices;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Helio.UI;

// A short red note in a field that refused what was typed ("not a number", "unknown foo"). It stands in the
// field for a moment and then the real value comes back. Pressing the field while the note is up hands back
// the typed text, caret at the end, so a typo is fixed rather than typed again. The field's value is never
// touched. -xlinka
public static class FieldHint
{
    private const float Seconds = 2.5f;
    private const float MinNoteSize = 8f;

    private sealed class Note
    {
        public bool Up;
        public int Serial;
        public string Typed = string.Empty;
        public string Message = string.Empty;
        public color Normal;
        public bool AutoSized;
        public float AutoSizeMin;
        public Text? Text;
        public Action? Restore;
        public bool Hooked;
    }

    private static readonly ConditionalWeakTable<TextInput, Note> Notes = new();

    // restore puts the field's real value back once the note has had its moment.
    public static void Show(TextInput input, string typed, string message, Action restore)
    {
        if (input == null || input.IsDestroyed || string.IsNullOrEmpty(message))
            return;
        var note = Notes.GetValue(input, _ => new Note());
        var text = ShownText(input);
        if (!note.Up && text != null)
        {
            note.Normal = text.Color.Value;
            note.AutoSized = text.AutoSizeHorizontal.Value;
            note.AutoSizeMin = text.AutoSizeMin.Value;
        }
        note.Up = true;
        note.Serial++;
        note.Typed = typed ?? string.Empty;
        note.Message = message;
        note.Text = text;
        note.Restore = restore;
        if (!note.Hooked)
        {
            note.Hooked = true;
            input.EditingStarted += OnEditingStarted;
        }
        // A narrow field (one lane of a vector) shrinks the note to fit rather than cutting it.
        if (text != null && !text.IsDestroyed)
        {
            text.Color.Value = DashTheme.Negative;
            text.AutoSizeHorizontal.Value = true;
            text.AutoSizeMin.Value = MinNoteSize;
        }
        input.Text.Value = message;

        int serial = note.Serial;
        input.RunInSeconds(Seconds, () => Expire(input, serial));
    }

    public static bool IsShowing(TextInput? input)
        => input != null && Notes.TryGetValue(input, out var note) && note.Up;

    // Takes the note down without restoring anything; the caller is about to write the field itself.
    public static void Clear(TextInput? input)
    {
        if (input == null || !Notes.TryGetValue(input, out var note) || !note.Up)
            return;
        note.Up = false;
        if (note.Text != null && !note.Text.IsDestroyed)
        {
            note.Text.Color.Value = note.Normal;
            note.Text.AutoSizeHorizontal.Value = note.AutoSized;
            note.Text.AutoSizeMin.Value = note.AutoSizeMin;
        }
    }

    private static void Expire(TextInput input, int serial)
    {
        if (!Notes.TryGetValue(input, out var note) || !note.Up || note.Serial != serial)
            return;
        if (input.IsDestroyed)
            return;
        Clear(input);
        if (!input.IsFocused)
            note.Restore?.Invoke();
    }

    private static void OnEditingStarted(TextInput input)
    {
        if (!Notes.TryGetValue(input, out var note) || !note.Up)
            return;
        bool showingNote = string.Equals(input.Text.Value, note.Message, StringComparison.Ordinal);
        Clear(input);
        if (!showingNote)
            return;
        input.Text.Value = note.Typed;
        input.SelectRange(note.Typed.Length, note.Typed.Length);
    }

    private static Text? ShownText(TextInput input)
        => input.ContentDrive.Target is SyncElement { Parent: Text text } && !text.IsDestroyed ? text : null;
}
