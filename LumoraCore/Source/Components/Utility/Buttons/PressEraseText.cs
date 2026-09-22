// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

[ComponentCategory("Utility/Buttons")]
public class PressEraseText : ButtonAction
{
    public readonly SyncRef<IField<string>> TargetString;

    public readonly Sync<int> Count;

    // Remove from the front instead of the end.
    public readonly Sync<bool> FromStart;

    public PressEraseText()
    {
        TargetString = new SyncRef<IField<string>>(this);
        Count = new Sync<int>(this, 1);
        FromStart = new Sync<bool>(this, false);
    }

    protected override void Pressed(User? actor)
    {
        var target = TargetString.Target;
        if (target == null || !target.CanWrite)
            return;

        string current = target.Value ?? "";
        if (current.Length == 0)
            return;

        // Clamp the count so a backspace held past the start of the string is a no-op rather than a
        // range exception on the pressing user's peer.
        int count = LuminaMath.Clamp(Count.Value, 0, current.Length);
        target.Value = FromStart.Value ? current[count..] : current[..(current.Length - count)];
    }
}
