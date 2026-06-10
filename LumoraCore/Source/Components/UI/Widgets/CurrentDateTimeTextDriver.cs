// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;

namespace Lumora.Core.Components.UI;

[ComponentCategory("Utility")]
public class CurrentDateTimeTextDriver : Component
{
    public readonly SyncRef<Text> Target;
    public readonly Sync<string> Format;

    public CurrentDateTimeTextDriver()
    {
        Target = new SyncRef<Text>(this);
        Format = new Sync<string>(this, "HH:mm");
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        var text = Target.Target;
        if (text == null) return;
        var next = System.DateTime.Now.ToString(Format.Value);
        if (text.Content.Value != next)
            text.Content.Value = next;
    }
}
