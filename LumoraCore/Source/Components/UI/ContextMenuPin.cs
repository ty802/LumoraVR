// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;

namespace Lumora.Core.Components.UI;

// Which hand's ring a pinned entry shows on.
public enum ContextMenuHand
{
    Either,
    Left,
    Right,
}

// Puts an entry on the root ring of whoever this sits under: the wearer of the avatar it is part of, the
// holder of the object. Anyone else's ring never sees it. -xlinka
[ComponentCategory("UI/Context Menu")]
public class ContextMenuPin : ContextMenuItemSource
{
    public readonly SyncRef<ContextMenuEntry> Entry;
    // Only on the ring this hand summons.
    public readonly Sync<ContextMenuHand> Side;
    // Not on the ring of a hand holding a tool.
    public readonly Sync<bool> NotWithTool;
    // Not on the ring of the hand its wearer leads with, or of the other one.
    public readonly Sync<bool> NotOnPrimaryHand;
    public readonly Sync<bool> NotOnSecondaryHand;

    public ContextMenuPin()
    {
        Entry = new SyncRef<ContextMenuEntry>(this);
        Side = new Sync<ContextMenuHand>(this, ContextMenuHand.Either);
        NotWithTool = new Sync<bool>(this, false);
        NotOnPrimaryHand = new Sync<bool>(this, false);
        NotOnSecondaryHand = new Sync<bool>(this, false);
    }

    public override void PopulateContextMenu(ContextMenuPage page, ContextMenuContext context)
    {
        var entry = Entry.Target;
        if (entry == null || entry.IsDestroyed || !entry.Enabled.Value)
            return;
        var owner = Slot?.ActiveUserRoot;
        if (owner == null || !ReferenceEquals(owner.ActiveUser, World?.LocalUser))
            return;
        var side = Side.Value;
        if (side != ContextMenuHand.Either && context.Side != (side == ContextMenuHand.Left ? Chirality.Left : Chirality.Right))
            return;
        if (NotWithTool.Value && context.HandHasTool(owner.Slot))
            return;
        if (context.Side is { } summoning && (NotOnPrimaryHand.Value || NotOnSecondaryHand.Value))
        {
            bool primary = summoning == EngineSettings.PrimaryHand;
            if (primary ? NotOnPrimaryHand.Value : NotOnSecondaryHand.Value)
                return;
        }
        page.AddItem(entry.ToMenuItem());
    }
}
