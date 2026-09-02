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
// the value replicates through the synced Text field. Mouse-drag selection is future work.
//
// The in-VR keyboard types through the public TypeString/PressBackspace/PressEnter/PressTab
// surface below, which runs the SAME insert/delete helpers the physical keyboard does. Keep it
// that way: a second copy of the caret and selection rules is how the two input paths end up
// disagreeing about where the caret is. -xlinka
[ComponentCategory("UI/Helio/Interaction")]
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

    // Caret index into the CURRENT value, for anything drawing its own view of this field (the VR
    // keyboard's preview strip). The child Text carries the same number in CaretPosition, but that one
    // is masked-string relative and only written while focused.
    public int CaretIndex => _caret;

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
        ClampCaret(value);

        bool shift = kb.IsKeyPressed(Key.LeftShift) || kb.IsKeyPressed(Key.RightShift);
        bool changed = false;
        bool caretMoved = false;

        // Ctrl+C/X/V run the SAME three methods the VR keyboard's Copy/Paste keys call. One path, so the
        // two keyboards can never end up with different ideas of what copy means on a field. Handled
        // before the typed-text insert and returning out of the frame: the shortcut has consumed the
        // keystroke, and letting it fall through would type a stray character on top of the paste. -xlinka
        if (kb.IsKeyPressed(Key.LeftControl) || kb.IsKeyPressed(Key.RightControl))
        {
            if (kb.IsKeyJustPressed(Key.C)) { CopySelection(); return; }
            if (kb.IsKeyJustPressed(Key.X)) { CutSelection(); return; }
            if (kb.IsKeyJustPressed(Key.V)) { PasteClipboard(); return; }
        }

        // Typed characters: replace the selection (if any), then insert at the caret.
        string typed = kb.GetTypedText();
        if (!string.IsNullOrEmpty(typed))
            changed |= InsertText(ref value, typed);

        if (kb.IsKeyJustPressed(Key.Backspace))
            changed |= DeleteBack(ref value);
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
        if (Multiline.Value && kb.IsKeyJustPressed(Key.Tab))
            changed |= InsertLiteral(ref value, IndentText);

        bool enter = kb.IsKeyJustPressed(Key.Return) || kb.IsKeyJustPressed(Key.KeypadEnter);
        bool escape = kb.IsKeyJustPressed(Key.Escape);

        if (enter && Multiline.Value)
        {
            changed |= InsertLiteral(ref value, "\n");
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

    // EDITING PRIMITIVES
    // Every insert and delete in this component goes through these three, whether the keystroke came
    // off the hardware keyboard in OnUpdate or off a VR key through the public surface further down. -xlinka

    private const string IndentText = "    ";

    private void ClampCaret(string value)
    {
        _caret = System.Math.Clamp(_caret, 0, value.Length);
        if (_selStart > value.Length) _selStart = value.Length;
    }

    // Printable characters only: control codes and DEL are dropped, which is what keeps a stray
    // newline out of a single-line field. Newlines and indents come in through InsertLiteral.
    private bool InsertText(ref string value, string typed)
    {
        int max = MaxLength.Value;
        bool changed = false;
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
        return changed;
    }

    // Verbatim insert for the things the printable filter would eat (newline, tab indent).
    private bool InsertLiteral(ref string value, string literal)
    {
        int max = MaxLength.Value;
        if (max > 0 && value.Length >= max)
            return false;
        if (HasSelection())
            DeleteSelection(ref value);
        value = value.Insert(_caret, literal);
        _caret += literal.Length;
        return true;
    }

    private bool DeleteBack(ref string value)
    {
        if (HasSelection())
        {
            DeleteSelection(ref value);
            return true;
        }
        if (_caret <= 0)
            return false;
        value = value.Remove(_caret - 1, 1);
        _caret--;
        return true;
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

    // TYPING SURFACE
    // What a virtual key press calls. Each one clamps, runs the same primitive the hardware path runs,
    // then commits and redraws exactly as OnUpdate does - so change events, MaxLength and selection
    // replacement behave identically no matter which keyboard you typed on. All of them stand down
    // unless this field currently holds focus. -xlinka

    private bool CanType => !IsDestroyed && IsFocused && !ReadOnly.Value;

    public void TypeString(string text)
    {
        if (!CanType || string.IsNullOrEmpty(text))
            return;
        string value = Text.Value ?? string.Empty;
        ClampCaret(value);
        if (InsertText(ref value, text))
            ApplyValue(value);
        UpdateDisplay();
    }

    public void PressBackspace()
    {
        if (!CanType)
            return;
        string value = Text.Value ?? string.Empty;
        ClampCaret(value);
        if (DeleteBack(ref value))
            ApplyValue(value);
        UpdateDisplay();
    }

    // Newline in a multi-line field, submit in a single-line one - the Return key's own rule.
    public void PressEnter()
    {
        if (!CanType)
            return;
        if (!Multiline.Value)
        {
            Submit();
            return;
        }
        string value = Text.Value ?? string.Empty;
        ClampCaret(value);
        if (InsertLiteral(ref value, "\n"))
            ApplyValue(value);
        UpdateDisplay();
    }

    // Indent, and only in a multi-line field. Single-line Tab does nothing here because it does
    // nothing on the hardware keyboard either; inventing a focus-hop for the virtual key would make
    // the two keyboards behave differently on the same field. -xlinka
    public void PressTab()
    {
        if (!CanType || !Multiline.Value)
            return;
        string value = Text.Value ?? string.Empty;
        ClampCaret(value);
        if (InsertLiteral(ref value, IndentText))
            ApplyValue(value);
        UpdateDisplay();
    }

    // Relative caret step, selection dropped. Named apart from the private absolute MoveCaret so
    // nobody reads MoveCaret(3) as "move to index 3".
    public void MoveCaretBy(int delta)
    {
        if (IsDestroyed || !IsFocused || delta == 0)
            return;
        string value = Text.Value ?? string.Empty;
        ClampCaret(value);
        MoveCaret(_caret + delta, false, value.Length);
        UpdateDisplay();
    }

    // Same-column hop to the neighbouring line, straight off the helper the Up/Down keys run in
    // OnUpdate. A single-line field has no neighbour, so this lands on the document edge.
    public void MoveCaretLine(bool up)
    {
        if (IsDestroyed || !IsFocused)
            return;
        string value = Text.Value ?? string.Empty;
        ClampCaret(value);
        MoveCaret(CaretLineStep(value, _caret, up), false, value.Length);
        UpdateDisplay();
    }

    // Home/End, line-relative, off the same two helpers the hardware keys use.
    public void MoveCaretToLineEdge(bool start)
    {
        if (IsDestroyed || !IsFocused)
            return;
        string value = Text.Value ?? string.Empty;
        ClampCaret(value);
        MoveCaret(start ? LineStartOf(value, _caret) : LineEndOf(value, _caret), false, value.Length);
        UpdateDisplay();
    }

    // Set the selection from code. Shift+arrow off the hardware keyboard is otherwise the ONLY thing
    // in this component that can move the anchor, which leaves the copy/cut surface below with a main
    // branch nothing outside OnUpdate can reach. Anchor and caret both clamp into the value; equal
    // means no selection. -xlinka
    public void SelectRange(int anchor, int caret)
    {
        if (IsDestroyed || !IsFocused)
            return;
        string value = Text.Value ?? string.Empty;
        _caret = System.Math.Clamp(caret, 0, value.Length);
        int start = System.Math.Clamp(anchor, 0, value.Length);
        _selStart = start == _caret ? -1 : start;
        UpdateDisplay();
    }

    // CLIPBOARD
    // The platform clipboard is a runner-injected service, so there is nothing to reach on a headless
    // build and every method here degrades to a no-op instead of throwing. -xlinka

    // Test seam. A headless harness has no Engine, so there is no InputInterface to hang a clipboard
    // off and no way to exercise copy/paste at all without this. Null in every real session; the
    // platform's clipboard goes on InputInterface where the rest of the platform services live.
    public static IClipboardText? ClipboardOverride { get; set; }

    private static IClipboardText? Clipboard => ClipboardOverride ?? Engine.Current?.InputInterface?.ClipboardText;

    // Whether copy/paste can do anything at all right now. The VR keyboard greys its Paste key on this.
    public static bool ClipboardAvailable => Clipboard != null;

    private string SelectedText(string value)
    {
        if (!HasSelection())
            return string.Empty;
        int s = System.Math.Clamp(_selStart < _caret ? _selStart : _caret, 0, value.Length);
        int e = System.Math.Clamp(_selStart < _caret ? _caret : _selStart, 0, value.Length);
        return e > s ? value.Substring(s, e - s) : string.Empty;
    }

    // The selection if there is one, the whole value if there is not - what every editor does when you
    // hit copy without having selected anything. Returns what it put on the clipboard so a caller can
    // see the result without reading the OS clipboard back, which is also what makes it testable.
    public string CopySelection()
    {
        if (IsDestroyed || !IsFocused)
            return string.Empty;
        string value = Text.Value ?? string.Empty;
        ClampCaret(value);
        string copied = HasSelection() ? SelectedText(value) : value;
        if (copied.Length == 0)
            return string.Empty;
        Clipboard?.SetText(copied);
        return copied;
    }

    // Copy then delete, and ONLY with a selection. A bare cut that empties the whole field is one
    // mis-hit away from losing everything you typed, and there is no undo on a text field. -xlinka
    public string CutSelection()
    {
        if (!CanType || !HasSelection())
            return string.Empty;
        string value = Text.Value ?? string.Empty;
        ClampCaret(value);
        string copied = SelectedText(value);
        if (copied.Length == 0)
            return string.Empty;
        Clipboard?.SetText(copied);
        DeleteSelection(ref value);
        ApplyValue(value);
        UpdateDisplay();
        return copied;
    }

    // Paste rides TypeString, so MaxLength, the selection replace and the change event behave exactly as
    // if the text had been typed in. InsertText's printable filter drops control codes, which is what
    // strips newlines out of a single-line field for free; a multi-line field has to put the breaks back
    // with the same literal insert the Enter key uses, since the filter would eat those too. -xlinka
    public bool PasteClipboard()
    {
        if (!CanType)
            return false;
        string text = Clipboard?.GetText() ?? string.Empty;
        if (text.Length == 0)
            return false;

        if (!Multiline.Value)
        {
            TypeString(text);
            return true;
        }

        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                string value = Text.Value ?? string.Empty;
                ClampCaret(value);
                if (InsertLiteral(ref value, "\n"))
                    ApplyValue(value);
                UpdateDisplay();
            }
            TypeString(lines[i]);
        }
        return true;
    }

    // Take focus without a click. The pointer path goes through OnPress; anything else that needs to
    // start a typing session (a harness, a panel handing off to a field) calls this.
    public void Focus()
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

    // The blur path: drop focus, release the keyboard, fire FocusLost, and commit nothing. Escape,
    // click-away and the VR keyboard's Close and Esc keys all land here.
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
