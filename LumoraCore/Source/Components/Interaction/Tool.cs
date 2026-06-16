// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;

namespace Lumora.Core.Components.Interaction;

[ComponentCategory("Interaction")]
[DefaultUpdateOrder(-1000)]
public abstract class Tool : Component
{
    public readonly Sync<Chirality> Side = new();

    public Chirality OtherSide => Side.Value == Chirality.Left ? Chirality.Right : Chirality.Left;

    public virtual Slot DirectionReference => Slot;

    public virtual Slot GripReference => Slot;

    public virtual Grabber? Grabber => null;

    public virtual InteractionLaser? Laser => null;

    public virtual bool PrimaryHeld => false;

    public virtual bool SecondaryHeld => false;

    public virtual bool GripHeld => false;

    public override void OnInit()
    {
        base.OnInit();
        Side.Value = Chirality.Right;
    }
}
