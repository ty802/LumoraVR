// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Localization;

namespace Lumora.Core.Components.UI;

public enum ModalOptionStyle
{
    Neutral,
    Accent,
    Destructive,
}

public sealed class ModalOption
{
    public LocaleText Label;
    public ModalOptionStyle Style;

    public ModalOption(LocaleText label, ModalOptionStyle style = ModalOptionStyle.Neutral)
    {
        Label = label;
        Style = style;
    }
}

// Index is which button was pressed, or -1 when the dialog was dismissed (scrim click, screen switch,
// the host being torn down). Callers branch on Confirmed, never on Text being non-empty - an empty
// answer to a prompt is still an answer.
public readonly struct ModalResult
{
    public readonly int Index;
    public readonly string Text;

    public ModalResult(int index, string text)
    {
        Index = index;
        Text = text ?? string.Empty;
    }

    public bool Confirmed => Index >= 0;
}

// What a caller hands the host. Deliberately a plain object rather than a component: modals are local,
// transient, per-peer UI and nothing about one belongs in the datamodel.
public sealed class ModalRequest
{
    public LocaleText Title;
    public LocaleText Message;
    public readonly List<ModalOption> Options = new();

    // Adds a text field between the message and the buttons.
    public bool Prompt;
    public LocaleText PromptPlaceholder;
    public string PromptInitial = string.Empty;

    // Which option a dismiss counts as when the scrim is clicked or the screen goes away. -1 reports a
    // dismiss as a dismiss, which is what a confirm dialog wants.
    public int CancelIndex = -1;

    // Which option the prompt field's Enter key fires. -1 means Enter only commits the text.
    public int DefaultIndex = -1;

    public bool DismissOnScrim = true;

    // The slot that asked for the dialog. The host hands it back on close so the caller can put its own
    // selection back where it was.
    public Slot? Invoker;

    public Action<ModalResult>? Completed;

    public ModalRequest AddOption(LocaleText label, ModalOptionStyle style = ModalOptionStyle.Neutral)
    {
        Options.Add(new ModalOption(label, style));
        return this;
    }
}
