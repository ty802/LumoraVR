// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// One press, as the receivers on the pressed thing's slot hear it: what was pressed, who pressed it, and where in
// the world.
public readonly struct PressEvent
{
    public readonly Component Source;
    public readonly User? Actor;
    public readonly float3 Point;

    public PressEvent(Component source, User? actor, in float3 point)
    {
        Source = source;
        Actor = actor;
        Point = point;
    }

    // Every enabled receiver on the slot hears it, and one that throws does not eat it for the rest.
    //
    // A press run inside its source's author scope passes sameAuthor. A receiver somebody else bolted onto that
    // slot is not part of what the author wired, and running it there would lend it the author's rights, so it
    // is left out. -xlinka
    public void Send(Slot? slot, bool sameAuthor = false)
    {
        if (slot == null || slot.IsDestroyed || Source == null)
            return;
        byte author = Source.ReferenceID.GetUserByte();
        foreach (var component in slot.GetComponents<Component>())
        {
            if (component is not IPressReceiver receiver || component.IsDestroyed || !component.Enabled.Value)
                continue;
            if (sameAuthor && component.ReferenceID.GetUserByte() != author)
                continue;
            try
            {
                receiver.OnPressed(in this);
            }
            catch (Exception ex)
            {
                Logging.Logger.Error($"Press receiver {component.GetType().Name} on '{slot.Name.Value}' threw: {ex.Message}");
            }
        }
    }
}
