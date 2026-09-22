// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core.Media;

namespace Lumora.Core.Components.Media;

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
