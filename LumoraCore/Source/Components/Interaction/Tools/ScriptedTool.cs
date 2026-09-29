// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Components.Interaction;

// A tool that does whatever a script makes of it. It is picked up and equipped like any other tool, and
// its buttons do nothing themselves: each press and release of primary and secondary is told to whatever
// listens, which is how a script's On Tool Pressed and On Tool Released hear it. While primary is down it
// is in use.
//
// UsesBeam keeps the hand's beam up while it is equipped, so the person can see where it points.
// ClaimsSecondary gives it the secondary button; without it that button stays the hand's own, which in
// VR is the menu. UsableWhileHolding lets it take presses while the other hand's grabber holds something.
//
// Presses happen on the machine of whoever holds it, and are told there. -xlinka
[ComponentCategory("Interaction/Tools")]
public sealed class ScriptedTool : ToolItem
{
    public readonly Sync<bool> UsesBeam;
    public readonly Sync<bool> ClaimsSecondary;
    public readonly Sync<bool> UsableWhileHolding;

    // The tool, whether it was secondary, and whether the button went down rather than up.
    public event Action<ScriptedTool, bool, bool>? Pressed;

    public ScriptedTool()
    {
        UsesBeam = new Sync<bool>(this, true);
        ClaimsSecondary = new Sync<bool>(this, true);
        UsableWhileHolding = new Sync<bool>(this, false);
    }

    public override bool UsesLaser => UsesBeam.Value;

    public override bool UsesSecondary => ClaimsSecondary.Value;

    public override bool CanUseWhenHolding => UsableWhileHolding.Value;

    public override bool OnPrimaryPress() => Tell(secondary: false, down: true);

    public override bool OnPrimaryRelease() => Tell(secondary: false, down: false);

    public override bool OnSecondaryPress() => Tell(secondary: true, down: true);

    public override bool OnSecondaryRelease() => Tell(secondary: true, down: false);

    // Claiming the press is what keeps the hand from also acting on whatever the beam is over.
    private bool Tell(bool secondary, bool down)
    {
        if (IsDestroyed || !Enabled.Value)
            return false;
        try
        {
            Pressed?.Invoke(this, secondary, down);
        }
        catch (Exception ex)
        {
            Logging.Logger.Error($"ScriptedTool on {ParentHierarchyToString()}: a listener threw: {ex.Message}");
        }
        return true;
    }
}
