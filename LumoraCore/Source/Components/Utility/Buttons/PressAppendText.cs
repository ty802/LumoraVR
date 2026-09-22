// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

[ComponentCategory("Utility/Buttons")]
public class PressAppendText : ButtonAction
{
    public readonly SyncRef<IField<string>> TargetString;

    public readonly Sync<string> Text;

    // Add the text at the front instead of the end.
    public readonly Sync<bool> Prepend;

    public PressAppendText()
    {
        TargetString = new SyncRef<IField<string>>(this);
        Text = new Sync<string>(this, "");
        Prepend = new Sync<bool>(this, false);
    }

    protected override void Pressed(User? actor)
    {
        var target = TargetString.Target;
        if (target == null || !target.CanWrite)
            return;
        string current = target.Value ?? "";
        string addition = Text.Value ?? "";
        target.Value = Prepend.Value ? addition + current : current + addition;
    }
}
