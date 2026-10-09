// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Media;

// One of a video player's companions: the chat column, the stats readout or the 360 orb. It rides beside
// the screen as the player's own child until somebody carries it off. Its grab never hands it back, so from
// then on it is an object of its own and stays wherever it was put. It still answers to its player: it is
// removed with the player, and hides while the player is put away (an undone spawn, a hidden parent). -xlinka
[ComponentCategory("Media")]
public sealed class VideoPlayerPart : Component
{
    public readonly SyncRef<VideoPlayer> Owner;
    public readonly Sync<VideoPlayerPartKind> Kind;

    // Set only when this part hid itself because its player went away, so the player brings back exactly
    // that and never one somebody switched off by hand. Local: every peer hides it for itself.
    private bool _hiddenForOwner;

    public VideoPlayerPart()
    {
        Owner = new SyncRef<VideoPlayer>(this);
        Kind = new Sync<VideoPlayerPartKind>(this, VideoPlayerPartKind.Chat);
    }

    public VideoPlayer? Player => Owner.Target is { IsDestroyed: false } player ? player : null;

    // Still beside the screen as the player's child, placed by it whenever the picture changes size.
    public bool Docked => Player is { } player && Slot is { IsDestroyed: false } slot && ReferenceEquals(slot.Parent, player.Slot);

    // The player keeps this one: a copy of it (duplicated, pasted) still names the player but is not its own.
    public bool Tracked => Player is { } player && player.Tracks(this);

    public override void Load(DataTreeNode node, LoadControl control)
    {
        base.Load(node, control);
        control.OnLoaded(DropIfStranded);
    }

    // Saved while hidden for a player that was put away and was not saved with it: nothing would ever show
    // it again, so it does not come back at all.
    private void DropIfStranded()
    {
        if (World is not { IsAuthority: true } || Owner.RawTarget != null || Slot is not { IsDestroyed: false } slot || slot.ActiveSelf.Value)
            return;
        try { slot.Destroy(); }
        catch (UnauthorizedAccessException) { }
    }

    internal void ShowForOwner()
    {
        if (!_hiddenForOwner || Slot is not { IsDestroyed: false } slot)
            return;
        _hiddenForOwner = false;
        if (!slot.ActiveSelf.Value)
            slot.ActiveSelf.SetValueSilently(true);
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        if (Slot is not { IsDestroyed: false } slot || World == null)
            return;

        // A part whose player was deleted goes with it. One that never had a player (saved on its own and
        // spawned again) is left alone: it works without one, and its close button removes it.
        var raw = Owner.RawTarget;
        if (raw != null && raw.IsDestroyed)
        {
            if (World.IsAuthority)
            {
                try { slot.Destroy(); }
                catch (UnauthorizedAccessException) { }
            }
            return;
        }

        var player = Player;
        if (player == null)
            return;

        // A copy the player does not keep stands on its own from here, even one made right beside the
        // screen: it no longer hides with the player, and its close button removes it instead of the
        // player's own.
        if (!player.Tracks(this))
        {
            if (World.IsAuthority)
            {
                try { Owner.Target = null!; }
                catch (UnauthorizedAccessException) { }
            }
            return;
        }
        if (Docked)
            return;

        // Off on its own, so the player's slot going inactive does not reach it: follow it by hand. The
        // player turns it back on from its own update once it is active again.
        bool ownerLive = player.Slot is { IsDestroyed: false } playerSlot && playerSlot.IsActive;
        if (!ownerLive && !_hiddenForOwner && slot.ActiveSelf.Value)
        {
            _hiddenForOwner = true;
            slot.ActiveSelf.SetValueSilently(false);
        }
    }
}
