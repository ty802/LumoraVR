// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.UI;

// The part of a list template that takes its entry. On each copy the list makes, this writes the entry's name
// and key into the text fields it points at, switches on the field it points at for being shown, and puts
// what the entry stands for into the reference member it points at, where the copy's own parts (a script, a
// pull source) read it. It writes when the entry changes and drives nothing: the values replicate like any
// other write. -xlinka
[ComponentCategory("UI")]
public class TemplateListRow : Component
{
    public readonly SyncRef<IField<string>> Name;
    public readonly SyncRef<IField<string>> Key;
    public readonly SyncRef<IField<bool>> Shown;

    // A reference member. The entry's element goes into it when that reference holds its kind.
    public readonly SyncRef<IWorldElement> Entity;

    public TemplateListRow()
    {
        Name = new SyncRef<IField<string>>(this);
        Key = new SyncRef<IField<string>>(this);
        Shown = new SyncRef<IField<bool>>(this);
        Entity = new SyncRef<IWorldElement>(this);
    }

    public void Write(in ListEntry entry)
    {
        WriteText(Name.Target, entry.Name);
        WriteText(Key.Target, entry.Key);
        if (Shown.Target is { CanWrite: true } shown && !shown.Value)
            shown.Value = true;
        if (Entity.Target is ISyncRef reference && !ReferenceEquals(reference.Target, entry.Entity))
        {
            if (entry.Entity == null)
                reference.Clear();
            else
                reference.TrySet(entry.Entity);
        }
    }

    private static void WriteText(IField<string>? field, string value)
    {
        if (field is { CanWrite: true } && field.Value != value)
            field.Value = value;
    }
}
