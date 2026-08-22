// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core.Assets;
using Lumora.Core.Assets.Animation;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

// Plays one animation clip, writing every bound channel through a drive each update.
//
// SYNC MODEL - derived, not streamed. The playhead is NOT a synced float advanced by an authority.
// Two synced values describe playback instead: an ANCHOR (a wall-clock instant plus the clip position
// at that instant) and a rate (Speed, Playing). Every peer computes
// position = anchorPosition + (now - anchorInstant) * speed, wrapped. Nothing moves per frame, so a
// clip costs sync traffic only when someone presses play, pauses, scrubs or changes speed - and a
// peer that joins an hour in computes the identical frame from the same two numbers instead of
// waiting for the next playhead packet. A streamed playhead would cost a float per animator per frame
// and still leave joiners on frame zero until the first update arrived.
//
// The anchor instant comes from the SESSION clock, not the world clock. World.Time starts at zero when
// each peer opens the world, so the same TotalTime is a different real instant on every machine and an
// anchor written in it would decode to a different frame per peer. World.SessionClock is the
// authority's reading with this peer's measured offset folded in, so an anchor written on one machine
// decodes to the same frame on all of them, to within the offset estimate rather than to within
// however far apart their wall clocks happen to be. The timeline keeps wall-clock magnitude, so anchors
// written before the session clock existed still decode correctly. Playback is tight enough for a body
// animation; it is still NOT sample-accurate audio sync. Only NowSeconds and the anchor
// writes know where the time comes from. -xlinka
[ComponentCategory("Animation")]
public class Animator : Component, ICustomInspectorUI
{
    public readonly AssetRef<AnimationAsset> Clip;

    public readonly Sync<bool> Playing;

    // negative plays backwards
    public readonly Sync<float> Speed;

    public readonly Sync<AnimationWrapMode> WrapMode;

    // seconds, at the instant named by AnchorTicks
    public readonly Sync<float> AnchorPosition;

    public readonly Sync<long> AnchorTicks;

    // one entry per bound channel
    public readonly SyncList<AnimationBinding> Bindings;

    // Typed writers, rebuilt whenever the clip or the binding list changes. Never persisted: they are
    // derived from the clip plus the bindings, both of which replicate.
    private readonly List<ITrackBinder> _binders = new();
    private bool _bindersValid;
    private int _boundCount;
    private int _unboundCount;

    // The clip instance the current binder table was built against. The asset behind Clip can be
    // replaced without the REF ever changing (the provider's URL moves, or the asset reloads), so a
    // ref-change event alone would leave binders pointing into a discarded clip's tracks.
    private AnimationClip? _boundClip;

    // Frames until the next retry while something is still unresolved. A joining peer receives the
    // binding elements before their target refs finish resolving, so the first rebuild legitimately
    // finds nothing to drive and has to be retried - but retrying every frame would rebuild the whole
    // table (and allocate a binder per bound track) forever whenever a target is permanently gone. -xlinka
    private int _retryCountdown;
    private const int RetryFrames = 30;

    public Animator()
    {
        Clip = new AssetRef<AnimationAsset>(this);
        Playing = new Sync<bool>(this, false);
        Speed = new Sync<float>(this, 1f);
        WrapMode = new Sync<AnimationWrapMode>(this, AnimationWrapMode.Loop);
        AnchorPosition = new Sync<float>(this, 0f);
        AnchorTicks = new Sync<long>(this, 0L);
        Bindings = new SyncList<AnimationBinding>();
    }

    public AnimationClip? CurrentClip => Clip.Asset?.Clip;

    // seconds; 0 with no clip
    public float Duration => CurrentClip?.Duration ?? 0f;

    public int BoundTrackCount => _boundCount;

    // target destroyed, or never bound
    public int UnboundTrackCount => _unboundCount;

    private double NowSeconds => World?.SessionClock.SessionSeconds
        ?? DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;

    // stored as ticks so it survives a save at full precision
    private long NowTicks => (long)(NowSeconds * TimeSpan.TicksPerSecond);

    // wrapped into the clip; setting it re-anchors so a scrub replicates as two numbers, not a stream
    public float Position
    {
        get => AnimationWrap.Wrap(RawPosition, Duration, WrapMode.Value);
        set => Reanchor(value, Playing.Value);
    }

    // unwrapped; used to tell whether a non-looping clip has run out
    public float RawPosition
    {
        get
        {
            if (!Playing.Value || AnchorTicks.Value == 0L)
            {
                return AnchorPosition.Value;
            }
            double elapsed = NowSeconds - AnchorTicks.Value / (double)TimeSpan.TicksPerSecond;
            return AnchorPosition.Value + (float)(elapsed * Speed.Value);
        }
    }

    public bool IsFinished => AnimationWrap.IsFinished(RawPosition, Duration, WrapMode.Value);

    public void Play() => Reanchor(Position, true);

    public void Pause() => Reanchor(Position, false);

    public void Stop() => Reanchor(0f, false);

    public void Restart() => Reanchor(0f, true);

    public void Reanchor(float position, bool playing)
    {
        if (!float.IsFinite(position))
        {
            position = 0f;
        }
        AnchorPosition.Value = position;
        AnchorTicks.Value = NowTicks;
        Playing.Value = playing;
    }

    public override void OnAwake()
    {
        base.OnAwake();

        // Both inputs to the binder table replicate, so every peer rebuilds independently and lands on
        // the same table without any of it being sent.
        Clip.OnValueChange += _ => _bindersValid = false;
        Bindings.ElementsAdded += (_, _, _) => _bindersValid = false;
        Bindings.ElementsRemoved += (_, _, _) => _bindersValid = false;

        // Speed changes must re-anchor or the playhead jumps: the old anchor's elapsed time would be
        // replayed at the new rate. Re-anchoring at the current position keeps the frame continuous.
        Speed.OnChanged += _ =>
        {
            if (Playing.Value)
            {
                AnchorPosition.Value = Position;
                AnchorTicks.Value = NowTicks;
            }
        };
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        var clip = CurrentClip;
        if (clip == null || clip.TrackCount == 0)
        {
            return;
        }

        // A different clip instance invalidates the table even when the ref never moved.
        if (!ReferenceEquals(clip, _boundClip))
        {
            _bindersValid = false;
        }

        if (!_bindersValid || (_unboundCount > 0 && --_retryCountdown <= 0))
        {
            RebuildBinders(clip);
        }
        if (_binders.Count == 0)
        {
            return;
        }

        float time = Position;
        for (int i = 0; i < _binders.Count; i++)
        {
            _binders[i].Apply(time);
        }
    }

    // BINDING

    public int AutoBind(Slot root)
    {
        var clip = CurrentClip;
        if (clip == null)
        {
            LumoraLogger.Warn("Animator.AutoBind: clip is not loaded yet; nothing to bind");
            return 0;
        }
        return AutoBind(root, clip);
    }

    // used by importers that already hold the built clip, skipping the asset round trip through the
    // local DB; bindings are track-index based so they still match once the clip arrives via Clip
    public int AutoBind(Slot root, AnimationClip clip)
    {
        if (root == null)
        {
            LumoraLogger.Warn("Animator.AutoBind: null root");
            return 0;
        }
        if (clip == null)
        {
            LumoraLogger.Warn("Animator.AutoBind: null clip");
            return 0;
        }

        Bindings.Clear();

        // One name sweep, reused for every track. A per-track FindChild would be O(tracks * slots),
        // which on a full-body clip is tens of thousands of walks for a table we can build once.
        var byName = new Dictionary<string, Slot>(StringComparer.Ordinal);
        IndexSubtree(root, byName);

        int bound = 0;
        for (int i = 0; i < clip.TrackCount; i++)
        {
            var track = clip[i];
            if (!byName.TryGetValue(track.Node, out var slot))
            {
                continue;
            }

            var field = ResolveProperty(slot, track.Property);
            if (field == null || field.ValueType != track.ValueType)
            {
                continue;
            }

            // A field already under someone else's drive would be refused by the link manager anyway;
            // skipping it here keeps the binding list honest about what is actually driving.
            if (field.IsDriven)
            {
                continue;
            }

            var binding = Bindings.Add();
            binding.TrackIndex.Value = i;
            binding.Target.DriveTarget(field);
            bound++;
        }

        _bindersValid = false;
        LumoraLogger.Log($"Animator.AutoBind: bound {bound}/{clip.TrackCount} tracks under '{root.SlotName.Value}'");
        return bound;
    }

    private static void IndexSubtree(Slot slot, Dictionary<string, Slot> byName)
    {
        if (slot == null)
        {
            return;
        }
        // First wins, matching how the model importer resolves duplicate node names to slots.
        var name = slot.SlotName.Value;
        if (!string.IsNullOrEmpty(name) && !byName.ContainsKey(name))
        {
            byName[name] = slot;
        }
        foreach (var child in slot.Children)
        {
            IndexSubtree(child, byName);
        }
    }

    // recognized forms: Position/Rotation/Scale (transform), BlendShape.<name> (skinned renderer
    // weight), <Component>.<Member>, or a bare slot member name
    public static IField? ResolveProperty(Slot slot, string property)
    {
        if (slot == null || string.IsNullOrEmpty(property))
        {
            return null;
        }

        switch (property)
        {
            case "Position":
                return slot.LocalPosition;
            case "Rotation":
                return slot.LocalRotation;
            case "Scale":
                return slot.LocalScale;
        }

        int dot = property.IndexOf('.');
        if (dot <= 0 || dot >= property.Length - 1)
        {
            return slot.TryGetField(property);
        }

        string head = property.Substring(0, dot);
        string tail = property.Substring(dot + 1);

        if (head == "BlendShape")
        {
            var renderer = slot.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null)
            {
                return null;
            }
            int index = renderer.GetBlendShapeIndex(tail);
            if (index < 0 || index >= renderer.BlendShapeWeights.Count)
            {
                return null;
            }
            return renderer.BlendShapeWeights.GetElement(index);
        }

        foreach (var component in slot.Components)
        {
            if (component != null && component.GetType().Name == head)
            {
                return component.TryGetField(tail);
            }
        }
        return null;
    }

    private void RebuildBinders(AnimationClip clip)
    {
        _binders.Clear();
        _boundCount = 0;
        _unboundCount = 0;

        for (int i = 0; i < Bindings.Count; i++)
        {
            var binding = Bindings[i];
            if (binding == null)
            {
                continue;
            }

            int trackIndex = binding.TrackIndex.Value;
            if (trackIndex < 0 || trackIndex >= clip.TrackCount)
            {
                _unboundCount++;
                continue;
            }

            var field = binding.Target.Field;
            if (field == null)
            {
                // Not granted yet (the ref may still be resolving on this peer) or the target is gone.
                _unboundCount++;
                continue;
            }

            var binder = CreateBinder(field.ValueType);
            if (binder == null || !binder.Setup(binding.Target, field, clip[trackIndex]))
            {
                _unboundCount++;
                continue;
            }

            _binders.Add(binder);
            _boundCount++;
        }

        _bindersValid = true;
        _boundClip = clip;
        _retryCountdown = RetryFrames;
    }

    // TYPED WRITE PATH
    // A binding knows its field's type only at runtime, so the write goes through a per-type binder
    // built once and cached. This is what keeps sampling and writing allocation-free per frame.

    private interface ITrackBinder
    {
        bool Setup(AnyFieldDrive drive, IField field, AnimationTrack track);
        void Apply(float time);
    }

    private sealed class TrackBinder<T> : ITrackBinder
    {
        private AnyFieldDrive _drive = null!;
        private SyncField<T> _field = null!;
        private IAnimationTrack<T> _track = null!;

        public bool Setup(AnyFieldDrive drive, IField field, AnimationTrack track)
        {
            if (field is not SyncField<T> typedField || track is not IAnimationTrack<T> typedTrack)
            {
                return false;
            }
            _drive = drive;
            _field = typedField;
            _track = typedTrack;
            return true;
        }

        public void Apply(float time)
        {
            // IsLinkValid is the guard that a refused or released link writes nothing.
            if (!_drive.IsLinkValid || _field.IsDestroyed)
            {
                return;
            }
            if (_track.Evaluate(time, out var value))
            {
                // Local-only: the clip, the anchor and the bindings all replicate, so every peer
                // derives this same value itself. Broadcasting it would double the traffic and fight
                // the remote peer's own computation.
                _field.SetDrivenValueLocal(value);
            }
        }
    }

    private static readonly Dictionary<Type, Func<ITrackBinder>> _binderFactories = new();

    private static ITrackBinder? CreateBinder(Type valueType)
    {
        if (valueType == null)
        {
            return null;
        }

        Func<ITrackBinder>? factory;
        lock (_binderFactories)
        {
            if (!_binderFactories.TryGetValue(valueType, out factory))
            {
                if (!AnimationCodecs.IsSupported(valueType))
                {
                    return null;
                }
                var closed = typeof(TrackBinder<>).MakeGenericType(valueType);
                factory = () => (ITrackBinder)Activator.CreateInstance(closed)!;
                _binderFactories[valueType] = factory;
            }
        }
        return factory();
    }

    // INSPECTOR

    public void BuildInspectorBody(UIBuilder ui)
    {
        var clip = CurrentClip;
        if (clip == null)
        {
            AddStatRow(ui, "Clip", Clip.Target == null ? "none assigned" : "loading...");
            return;
        }

        AddStatRow(ui, "Clip", string.IsNullOrEmpty(clip.Name) ? "(unnamed)" : clip.Name);
        AddStatRow(ui, "Duration", $"{clip.Duration:0.###} s");
        if (clip.Framerate > 0f)
        {
            AddStatRow(ui, "Source rate", $"{clip.Framerate:0.##} fps");
        }
        AddStatRow(ui, "Tracks", clip.TrackCount.ToString());
        AddStatRow(ui, "Bound", $"{_boundCount} driving");
        if (_unboundCount > 0)
        {
            AddStatRow(ui, "Unbound", $"{_unboundCount} not resolved");
        }
        AddStatRow(ui, "Position", $"{Position:0.###} s");
        AddStatRow(ui, "State", Playing.Value ? $"playing x{Speed.Value:0.##}" : "paused");
    }

    private static void AddStatRow(UIBuilder ui, string label, string value)
    {
        // Theme from the hosting panel's UI tree, NOT this component's world slot: the animator's slot
        // has no UITheme above it, and text without a font renders nothing.
        InspectorUI.FixedRow(ui.Root, label, 24f, out var rowUi, ui.Root);
        rowUi.PushStyle();
        rowUi.MinWidth(150f);
        rowUi.PreferredWidth(190f);
        rowUi.FlexibleWidth(0f);
        var labelText = rowUi.Text(label, InspectorUI.FontSize - 1f, InspectorUI.MutedColor);
        InspectorUI.FillParent(labelText.RectTransform!);
        labelText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        labelText.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        rowUi.PopStyle();
        rowUi.PushStyle();
        rowUi.FlexibleWidth(1f);
        var valueText = rowUi.Text(value, InspectorUI.FontSize - 1f, InspectorUI.TextColor);
        InspectorUI.FillParent(valueText.RectTransform!);
        valueText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        valueText.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        rowUi.PopStyle();
    }
}
