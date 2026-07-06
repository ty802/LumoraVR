// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Input;

namespace Helio.UI;

/// <summary>
/// An editable text field. Click to focus, then type; Backspace/Delete remove,
/// arrows/Home/End move the caret, Shift+move selects, typing replaces a selection,
/// Enter submits (or inserts a newline when <see cref="Multiline"/>), Escape cancels.
/// Drives a child "Text" component's content and its caret/selection visuals; shows
/// <see cref="Placeholder"/> when empty and unfocused. Maps to an HTML input/textarea.
/// </summary>
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
    /// <summary>Mask each character as a bullet (password field).</summary>
    public readonly Sync<bool> Mask;
    /// <summary>Display-only: can't be focused or edited.</summary>
    public readonly Sync<bool> ReadOnly;

    public readonly SyncDelegate<Action<TextInput, string>> ChangeAction;
    public readonly SyncDelegate<Action<TextInput, string>> SubmitAction;

    public event Action<TextInput>? EditingStarted;
    public event Action<TextInput, string>? EditingChanged;
    public event Action<TextInput, string>? EditingFinished;

    public FieldDrive<string>? ContentDrive { get; private set; }

    private static TextInput? _focused;

    private Helio.UI.Text? _text;
    private int _caret;
    private int _selStart = -1; // selection anchor, -1 = no selection

    public bool IsFocused => ReferenceEquals(_focused, this);

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

    public override void OnAwake()
    {
        base.OnAwake();
        ContentDrive = new FieldDrive<string>(World);
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
            _focused = null;
        ContentDrive?.Release();
        ContentDrive = null;
        _text = null;
        base.OnDestroy();
    }

    private void RebindVisuals()
    {
        _text = Slot?.FindChild("Text", recursive: false)?.GetComponent<Helio.UI.Text>();
        if (_text != null)
            ContentDrive?.DriveTarget(_text.Content);
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
        if (kb.IsKeyJustPressed(Key.Home)) { MoveCaret(0, shift); caretMoved = true; }
        if (kb.IsKeyJustPressed(Key.End)) { MoveCaret(value.Length, shift, value.Length); caretMoved = true; }

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
        EditingStarted?.Invoke(this);
    }

    public void Unfocus()
    {
        bool was = ReferenceEquals(_focused, this);
        if (was)
            _focused = null;
        _selStart = -1;
        UpdateDisplay();
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

        ContentDrive?.SetValue(content);

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
