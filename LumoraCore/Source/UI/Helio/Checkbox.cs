// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;

namespace Helio.UI;

public sealed class Checkbox : InteractionElement
{
    public readonly Sync<bool> IsChecked;

    public event Action<Checkbox, bool>? ValueChanged;

    public Checkbox()
    {
        IsChecked = new Sync<bool>(this, false);
    }

    protected override void OnSubmit(in UIInteractionContext context)
    {
        IsChecked.Value = !IsChecked.Value;
        ValueChanged?.Invoke(this, IsChecked.Value);
    }
}
