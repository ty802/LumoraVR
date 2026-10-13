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

    // Which row of a pick list was chosen, or -1 when the dialog had no list.
    public readonly int Choice;

    public ModalResult(int index, string text, int choice)
    {
        Index = index;
        Text = text ?? string.Empty;
        Choice = choice;
    }

    public ModalResult(int index, string text)
    {
        Index = index;
        Text = text ?? string.Empty;
        Choice = -1;
    }

    public bool Confirmed => Index >= 0;
}

// What a caller hands the host. Deliberately a plain object rather than a component: modals are local,
// transient, per-peer UI and nothing about one belongs in the datamodel.
// One row in a pick list: what it says, and a second line under it saying what it is.
public sealed class ModalChoice
{
    public LocaleText Label;
    public LocaleText Detail;

    public ModalChoice(LocaleText label, LocaleText detail = default)
    {
        Label = label;
        Detail = detail;
    }
}

public sealed class ModalRequest
{
    public LocaleText Title;
    public LocaleText Message;
    public readonly List<ModalOption> Options = new();

    // A VERTICAL PICK LIST, for a dialog whose question is "which one".
    //
    // The options row is horizontal and holds two or three buttons; it cannot hold eight audio
    // devices, and the first thing anybody needs from a stream dialog is to pick which device. So a
    // dialog can carry a list as well, rendered as rows between the message and the buttons, and the
    // selection arrives in the result. -xlinka
    public readonly List<ModalChoice> Choices = new();

    // Which row starts selected, and what comes back if the dialog is confirmed without touching it.
    public int SelectedChoice = -1;

    // Raised the moment a row is picked, before any button is pressed - a picker that previews
    // (a device meter, say) wants to know immediately rather than at the end.
    public Action<int>? ChoiceChanged;

    // Compact pickers filter by name/detail and commit through ChoiceChanged on one click.
    // Dismissing them never calls ChoiceChanged.
    public bool SearchChoices;
    public bool CloseOnChoice;

    public ModalRequest AddChoice(LocaleText label, LocaleText detail = default)
    {
        Choices.Add(new ModalChoice(label, detail));
        return this;
    }

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
