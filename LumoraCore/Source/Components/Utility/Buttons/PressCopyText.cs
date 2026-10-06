// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Utility;

// Puts a text field's value on the clipboard of whoever presses the button on this slot. The press runs on
// their machine, which is the only clipboard it touches. -xlinka
[ComponentCategory("Utility/Buttons")]
public sealed class PressCopyText : ButtonAction
{
    public readonly SyncRef<IField<string>> Source;

    public PressCopyText()
    {
        Source = new SyncRef<IField<string>>(this);
    }

    protected override void Pressed(User? actor)
    {
        if (actor != null && !ReferenceEquals(actor, World?.LocalUser))
            return;
        var source = Source.Target;
        if (source == null || source.IsDestroyed)
            return;
        Engine.Current?.InputInterface?.ClipboardText?.SetText(source.Value ?? string.Empty);
    }
}
