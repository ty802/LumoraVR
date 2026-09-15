// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core.Media;

namespace Lumora.Core.Components.Media;

// Turns an interaction element on this slot into a transport control for any IPlayable: a video, an
// animation, a sound. Four separate actions because a scrub pad and a play button are the same
// component with different fills - press to play, leave to pause, whatever the panel wants.
//
// The element's events are plain local events subscribed per peer, not a SyncDelegate: every peer runs
// its own OnStart and binds its own copy, so a duplicated panel is wired without anything crossing the
// wire and without a closure that a clone could not carry. -xlinka
[ComponentCategory("Media")]
public sealed class ButtonPlaybackAction : Component
{
    public readonly SyncRef<Component> Playback;
    public readonly Sync<PlaybackAction> OnPress;
    public readonly Sync<PlaybackAction> OnRelease;
    public readonly Sync<PlaybackAction> OnHover;
    public readonly Sync<PlaybackAction> OnLeave;

    private InteractionElement? _bound;

    public IPlayable? Target => Playback.Target as IPlayable;

    public ButtonPlaybackAction()
    {
        Playback = new SyncRef<Component>(this);
        OnPress = new Sync<PlaybackAction>(this, PlaybackAction.ToggleResumePause);
        OnRelease = new Sync<PlaybackAction>(this, PlaybackAction.None);
        OnHover = new Sync<PlaybackAction>(this, PlaybackAction.None);
        OnLeave = new Sync<PlaybackAction>(this, PlaybackAction.None);
    }

    public override void OnStart()
    {
        base.OnStart();
        Bind();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        Bind();
    }

    public override void OnDestroy()
    {
        Unbind();
        base.OnDestroy();
    }

    private void Bind()
    {
        var element = Slot?.GetComponent<InteractionElement>();
        if (ReferenceEquals(element, _bound))
            return;

        Unbind();
        if (element == null || element.IsDestroyed)
            return;

        _bound = element;
        element.Pressed += HandlePress;
        element.Released += HandleRelease;
        element.HoverEntered += HandleHover;
        element.HoverExited += HandleLeave;
    }

    private void Unbind()
    {
        if (_bound == null)
            return;
        _bound.Pressed -= HandlePress;
        _bound.Released -= HandleRelease;
        _bound.HoverEntered -= HandleHover;
        _bound.HoverExited -= HandleLeave;
        _bound = null;
    }

    private void HandlePress(UIInteractionContext context) => Run(OnPress.Value);
    private void HandleRelease(UIInteractionContext context) => Run(OnRelease.Value);
    private void HandleHover(UIInteractionContext context) => Run(OnHover.Value);
    private void HandleLeave(UIInteractionContext context) => Run(OnLeave.Value);

    public void Run(PlaybackAction action)
    {
        var target = Target;
        if (target == null)
            return;

        switch (action)
        {
            case PlaybackAction.Play:
                target.Play();
                break;
            case PlaybackAction.Resume:
                target.Resume();
                break;
            case PlaybackAction.Pause:
                target.Pause();
                break;
            case PlaybackAction.Stop:
                target.Stop();
                break;
            case PlaybackAction.ToggleResumePause:
                if (target.IsPlaying)
                    target.Pause();
                else
                    target.Resume();
                break;
            case PlaybackAction.TogglePlayStop:
                if (target.IsPlaying)
                    target.Stop();
                else
                    target.Play();
                break;
        }
    }
}

// A scrub surface: the whole rect is the timeline, and the press point along it is the position. Sits
// on any interaction element, so a bare image with a Button on it becomes a seek bar without a handle
// or a track to lay out.
//
// The point comes from PointIn, never the raw canvas point: a seek bar inside a scrolled list draws
// where its chunk offset puts it, and the unscrolled rect would read the press somewhere else entirely.
// A live stream has no length, so NormalizedPosition refuses the write and a scrub does nothing rather
// than jumping the head to zero. -xlinka
[ComponentCategory("Media")]
public sealed class ButtonPlaybackSeeker : Component
{
    public readonly SyncRef<Component> Playback;

    // Scrub up the rect instead of across it.
    public readonly Sync<bool> Vertical;

    // Keep seeking while the pointer is held. Off seeks once, on the press.
    public readonly Sync<bool> Continuous;

    private InteractionElement? _bound;

    public IPlayable? Target => Playback.Target as IPlayable;

    public ButtonPlaybackSeeker()
    {
        Playback = new SyncRef<Component>(this);
        Vertical = new Sync<bool>(this, false);
        Continuous = new Sync<bool>(this, true);
    }

    public override void OnStart()
    {
        base.OnStart();
        Bind();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        Bind();
    }

    public override void OnDestroy()
    {
        Unbind();
        base.OnDestroy();
    }

    private void Bind()
    {
        var element = Slot?.GetComponent<InteractionElement>();
        if (ReferenceEquals(element, _bound))
            return;

        Unbind();
        if (element == null || element.IsDestroyed)
            return;

        _bound = element;
        element.Pressed += HandlePoint;
        element.Dragged += HandleDrag;
    }

    private void Unbind()
    {
        if (_bound == null)
            return;
        _bound.Pressed -= HandlePoint;
        _bound.Dragged -= HandleDrag;
        _bound = null;
    }

    private void HandleDrag(UIInteractionContext context)
    {
        if (Continuous.Value)
            HandlePoint(context);
    }

    private void HandlePoint(UIInteractionContext context)
    {
        var target = Target;
        if (target == null || target.IsStreaming)
            return;

        var rect = _bound?.RectTransform?.LocalComputeRect;
        if (!rect.HasValue)
            return;

        bool vertical = Vertical.Value;
        float span = vertical ? rect.Value.height : rect.Value.width;
        if (span <= 0f)
            return;

        var point = context.PointIn(Slot);
        float t = vertical
            ? (point.y - rect.Value.yMin) / span
            : (point.x - rect.Value.xMin) / span;
        if (t < 0f) t = 0f;
        if (t > 1f) t = 1f;

        target.NormalizedPosition = t;
    }
}
