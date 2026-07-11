// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Input;

namespace Helio.UI;

// editable text field. click to focus, then type; backspace/delete remove, arrows/home/end move
// the caret, shift+move selects, typing replaces a selection, enter submits (or inserts a newline
// when Multiline), escape cancels. drives a child "Text" component's content and its
// caret/selection visuals; shows Placeholder when empty and unfocused.
// The caret + selection are REAL geometry rendered by the child Text (steady caret).
// Only the clicked instance edits (single static focus), reading the LOCAL keyboard;
// the value replicates through the synced Text field. Mouse-drag selection and a VR
// on-screen keyboard are future work. -xlinka
public sealed class TextInput : InteractionElement
{
    public readonly Sync<string> Text;
    public readonly Sync<string> Placeholder;
    public readonly Sync<bool> Multiline;
    public readonly Sync<int> MaxLength;
    // mask each character as a bullet (password field)
    public readonly Sync<bool> Mask;
    // display-only: can't be focused or edited
    public readonly Sync<bool> ReadOnly;

    public readonly SyncDelegate<Action<TextInput, string>> ChangeAction;
    public readonly SyncDelegate<Action<TextInput, string>> SubmitAction;

    public event Action<TextInput>? EditingStarted;
    public event Action<TextInput, string>? EditingChanged;
    public event Action<TextInput, string>? EditingFinished;
    // fires on ANY focus loss (enter, escape, click-away, destroy), unlike EditingFinished which
    // is submit-only. editors use it to close a typing session exactly once.
    public event Action<TextInput, string>? FocusLost;

    // drives the child Text's content. declared member: the target replicates and saves.
    public readonly FieldDrive<string> ContentDrive = new();

    private static TextInput? _focused;

    private Helio.UI.Text? _text;
    private int _caret;
    private int _selStart = -1; // selection anchor, -1 = no selection
    // The suppression set we registered with while focused. Cached so the release always hits the
    // SAME instance even if the focused world changes underneath us; no stuck-unable-to-walk state. -xlinka
    private Lumora.Core.Components.UserInputState? _suppressionState;

    public bool IsFocused => ReferenceEquals(_focused, this);

    // the input currently owning the local keyboard, if any. game-input readers (menu key, tool
    // hotkeys) stand down while this is non-null.
    public static TextInput? Focused => _focused;

    public TextInput()
    {
        Text = new Sync<string>(this, string.Empty);
        Placeholder = new Sync<string>(this, string.Empty);
        Multiline = new Sync<bool>(this, false);
        MaxLength = new Sync<int>(this, 0);
        Mask = new Sync<bool>(this, false);
        ReadOnly = new Sync<bool>(this, false);
        ChangeAction = new SyncDelegate<Action<TextInput, string>>(this);
        SubmitAction = new SyncDelegate<Action<TextInput, string>>(this);
    }

    public void SetChangeAction(Action<TextInput, string>? action)
    {
        if (action == null) return;
        if (action.Target is IWorldElement) ChangeAction.Target = action;
        else EditingChanged += action;
    }

    public void SetSubmitAction(Action<TextInput, string>? action)
    {
        if (action == null) return;
        if (action.Target is IWorldElement) SubmitAction.Target = action;
        else EditingFinished += action;
    }

    public override void OnStart()
    {
        base.OnStart();
        RebindVisuals();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        UpdateDisplay();
    }

    public override void OnDestroy()
    {
        if (ReferenceEquals(_focused, this))
        {
            _focused = null;
            // Destroyed mid-edit (pane rebuild): close the session and let go of the keyboard.
            FocusLost?.Invoke(this, Text.Value ?? string.Empty);
        }
        SetTypingSuppression(false);
        _text = null;
        base.OnDestroy();
    }

    private void RebindVisuals()
    {
        // _text is a per-peer handle for the caret/selection fields; the CONTENT link is a real member,
        // so only default it when nothing named a target.
        _text = Slot?.FindChild("Text", recursive: false)?.GetComponent<Helio.UI.Text>();
        if (_text != null && ContentDrive.ShouldApplyDefault)
            ContentDrive.DriveTarget(_text.Content);
        UpdateDisplay();
    }

    protected override void OnPress(in UIInteractionContext context)
    {
        if (!ReadOnly.Value)
            Focus();
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (!IsFocused)
            return;
        if (!CanInteract)
        {
            Unfocus();
            return;
        }

        // A focused field OWNS the keyboard: it reads keys raw, and the action map's keyboard source
        // is gated off for as long as this holds focus, so nothing else can see the same keystrokes.
        // That ownership is why the reads below are not action reads - routing them through the map
        // would mean gating the map on the very thing the map would have to drive. Modifiers here
        // (shift for selection) are text-editing state, not rebindable controls. -xlinka
        var kb = Engine.Current?.InputInterface?.Keyboard;
        if (kb == null)
            return;

        string value = Text.Value ?? string.Empty;
        _caret = System.Math.Clamp(_caret, 0, value.Length);
        if (_selStart > value.Length) _selStart = value.Length;

        int max = MaxLength.Value;
        bool shift = kb.IsKeyPressed(Key.LeftShift) || kb.IsKeyPressed(Key.RightShift);
        bool changed = false;
        bool caretMoved = false;

        // Typed characters: replace the selection (if any), then insert at the caret.
        string typed = kb.GetTypedText();
        if (!string.IsNullOrEmpty(typed))
        {
            foreach (char c in typed)
            {
                if (c < ' ' || c == (char)127)
                    continue;
                if (HasSelection())
                {
                    DeleteSelection(ref value);
                    changed = true;
                }
                if (max > 0 && value.Length >= max)
                    break;
                value = value.Insert(_caret, c.ToString());
                _caret++;
                changed = true;
            }
        }

        if (kb.IsKeyJustPressed(Key.Backspace))
        {
            if (HasSelection()) { DeleteSelection(ref value); changed = true; }
            else if (_caret > 0) { value = value.Remove(_caret - 1, 1); _caret--; changed = true; }
        }
        if (kb.IsKeyJustPressed(Key.Delete))
        {
            if (HasSelection()) { DeleteSelection(ref value); changed = true; }
            else if (_caret < value.Length) { value = value.Remove(_caret, 1); changed = true; }
        }

        if (kb.IsKeyJustPressed(Key.LeftArrow)) { MoveCaret(_caret - 1, shift); caretMoved = true; }
        if (kb.IsKeyJustPressed(Key.RightArrow)) { MoveCaret(_caret + 1, shift, value.Length); caretMoved = true; }
        // Home/End are line-relative (single-line has no newlines, so this is unchanged for it). Up/Down
        // keep the column and hop to the same spot on the neighbor line - the caret moves users expect
        // in a multi-line editor. -xlinka
        if (kb.IsKeyJustPressed(Key.Home)) { MoveCaret(LineStartOf(value, _caret), shift, value.Length); caretMoved = true; }
        if (kb.IsKeyJustPressed(Key.End)) { MoveCaret(LineEndOf(value, _caret), shift, value.Length); caretMoved = true; }
        if (kb.IsKeyJustPressed(Key.UpArrow)) { MoveCaret(CaretLineStep(value, _caret, up: true), shift, value.Length); caretMoved = true; }
        if (kb.IsKeyJustPressed(Key.DownArrow)) { MoveCaret(CaretLineStep(value, _caret, up: false), shift, value.Length); caretMoved = true; }

        // Tab in a multi-line field indents by spaces (Tab is stripped from typed text, so handle it here).
        if (Multiline.Value && kb.IsKeyJustPressed(Key.Tab) && (max <= 0 || value.Length < max))
        {
            if (HasSelection()) { DeleteSelection(ref value); }
            value = value.Insert(_caret, "    ");
            _caret += 4;
            changed = true;
        }

        bool enter = kb.IsKeyJustPressed(Key.Return) || kb.IsKeyJustPressed(Key.KeypadEnter);
        bool escape = kb.IsKeyJustPressed(Key.Escape);

        if (enter && Multiline.Value && (max <= 0 || value.Length < max))
        {
            if (HasSelection()) { DeleteSelection(ref value); }
            value = value.Insert(_caret, "\n");
            _caret++;
            changed = true;
            enter = false;
        }

        if (changed)
            ApplyValue(value);
        if (changed || caretMoved)
            UpdateDisplay();

        if (enter) { Submit(); return; }
        if (escape) { Unfocus(); return; }
    }

    // Index of the first char on the caret's line (just after the previous '\n', or 0).
    private static int LineStartOf(string s, int caret)
    {
        int i = System.Math.Clamp(caret, 0, s.Length) - 1;
        while (i >= 0 && s[i] != '\n') i--;
        return i + 1;
    }

    // Index of the '\n' ending the caret's line, or the string length for the last line.
    private static int LineEndOf(string s, int caret)
    {
        int i = System.Math.Clamp(caret, 0, s.Length);
        while (i < s.Length && s[i] != '\n') i++;
        return i;
    }

    // Same-column move to the previous/next line; clamps to that line's length. Returns the caret index
    // for the neighbor line, or the document edge when there is no neighbor. -xlinka
    private static int CaretLineStep(string s, int caret, bool up)
    {
        int lineStart = LineStartOf(s, caret);
        int column = caret - lineStart;
        if (up)
        {
            if (lineStart == 0)
                return 0; // already on the first line
            int prevEnd = lineStart - 1; // the '\n' before this line
            int prevStart = LineStartOf(s, prevEnd);
            return System.Math.Min(prevStart + column, prevEnd);
        }
        int lineEnd = LineEndOf(s, caret);
        if (lineEnd >= s.Length)
            return s.Length; // already on the last line
        int nextStart = lineEnd + 1;
        int nextEnd = LineEndOf(s, nextStart);
        return System.Math.Min(nextStart + column, nextEnd);
    }

    private bool HasSelection() => _selStart >= 0 && _selStart != _caret;

    private void DeleteSelection(ref string value)
    {
        int s = _selStart < _caret ? _selStart : _caret;
        int e = _selStart < _caret ? _caret : _selStart;
        s = System.Math.Clamp(s, 0, value.Length);
        e = System.Math.Clamp(e, 0, value.Length);
        if (e > s)
            value = value.Remove(s, e - s);
        _caret = s;
        _selStart = -1;
    }

    private void MoveCaret(int target, bool extendSelection, int limit = 0)
    {
        if (extendSelection)
        {
            if (_selStart < 0) _selStart = _caret; // start a selection from the old caret
        }
        else
        {
            _selStart = -1;
        }

        if (target < 0) target = 0;
        if (limit > 0 && target > limit) target = limit;
        _caret = target;
    }

    private void ApplyValue(string value)
    {
        if (value == (Text.Value ?? string.Empty))
            return;
        Text.Value = value; // OnChanges -> UpdateDisplay
        EditingChanged?.Invoke(this, value);
        ChangeAction.Target?.Invoke(this, value);
    }

    private void Focus()
    {
        if (ReferenceEquals(_focused, this))
            return;
        _focused?.Unfocus();
        _focused = this;
        _caret = (Text.Value ?? string.Empty).Length;
        _selStart = -1;
        UpdateDisplay();
        // Typing owns the keyboard: gate walking/crouch/hotkeys through the shared requester set
        // until focus is released.
        SetTypingSuppression(true);
        EditingStarted?.Invoke(this);
    }

    public void Unfocus()
    {
        bool was = ReferenceEquals(_focused, this);
        if (was)
            _focused = null;
        _selStart = -1;
        UpdateDisplay();
        if (was)
        {
            SetTypingSuppression(false);
            FocusLost?.Invoke(this, Text.Value ?? string.Empty);
        }
    }

    private void SetTypingSuppression(bool active)
    {
        if (active)
        {
            var state = Lumora.Core.Components.UserInputState.ForFocusedLocalUser;
            if (_suppressionState != null && !ReferenceEquals(_suppressionState, state))
                _suppressionState.SetDesktopInputSuppressed(this, false);
            _suppressionState = state;
            state?.SetDesktopInputSuppressed(this, true);
        }
        else
        {
            _suppressionState?.SetDesktopInputSuppressed(this, false);
            _suppressionState = null;
        }
    }

    private void Submit()
    {
        var value = Text.Value ?? string.Empty;
        Unfocus();
        EditingFinished?.Invoke(this, value);
        SubmitAction.Target?.Invoke(this, value);
    }

    private void UpdateDisplay()
    {
        string value = Text.Value ?? string.Empty;
        string masked = Mask.Value && value.Length > 0 ? new string('•', value.Length) : value;

        string content;
        if (IsFocused)
            content = masked;
        else if (value.Length == 0)
            content = Placeholder.Value ?? string.Empty;
        else
            content = masked;

        ContentDrive.SetValue(content);

        if (_text != null)
        {
            if (IsFocused)
            {
                _text.CaretPosition.Value = System.Math.Clamp(_caret, 0, content.Length);
                _text.SelectionStart.Value = HasSelection() ? System.Math.Clamp(_selStart, 0, content.Length) : -1;
            }
            else
            {
                if (_text.CaretPosition.Value != -1) _text.CaretPosition.Value = -1;
                if (_text.SelectionStart.Value != -1) _text.SelectionStart.Value = -1;
            }
        }
    }
}
