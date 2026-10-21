// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Assets;
using Lumora.Core.Assets.Animation;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.UI;
using Lumora.Core.Localization;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// Curve Studio: a spawned panel for editing curves, opened from an animation clip, an animator, or any
// curve field in the inspector. Tracks down the left rail, the selected track's curves in the graph with
// a time ruler underneath, and a key strip along the bottom for typing exact numbers.
//
// The graph is the editor. Press a key to select it and drag it, press empty space and drag to pan,
// press twice on empty space to drop a key there, drag in the ruler to scrub. The wheel zooms time,
// with shift the value axis; a stick does the same in VR. Smooth keys show tangent handles to aim.
//
// Who does what. Selection, the view and the playhead are synced fields here, so everyone looking at
// the panel sees the same thing. The keys are not: every machine builds its own source from the same
// target and draws from it, and only the machine whose user is pressing writes. A component curve
// writes straight into its list as you drag; a clip previews on the dragger's machine and saves a new
// clip on release (see ClipCurveSource). The labels and lists on the panel are synced UI, so only the
// authority writes them. -xlinka
[ComponentCategory("Utility/Inspectors")]
public sealed class CurveStudioPanel : Component, IInspectorActionContextHandler
{
    public readonly SyncRef<Component> Target;
    // Empty for a clip; the curve field's name on the target otherwise.
    public readonly Sync<string> Member;
    // Holds a clip loaded on every machine the panel is open on, so they all have keys to draw.
    public readonly AssetRef<AnimationAsset> Clip;

    public readonly Sync<int> Track;
    public readonly Sync<int> Channel;
    public readonly Sync<int> Key;
    public readonly Sync<bool> Solo;
    public readonly Sync<bool> Snap;
    public readonly Sync<bool> Preview;
    public readonly Sync<float2> TimeView;
    public readonly Sync<float2> ValueView;
    public readonly Sync<float> Playhead;
    [NonPersistent]
    public readonly Sync<string> Status;

    private readonly SyncRef<Slot> _trackHost;
    private readonly SyncRef<Slot> _chipHost;
    private readonly SyncRef<Slot> _timeAxis;
    private readonly SyncRef<Slot> _valueAxis;
    private readonly SyncRef<Text> _statusText;
    private readonly SyncRef<Text> _trackLabel;
    private readonly SyncRef<Text> _keyLabel;
    private readonly SyncRef<TextInput> _timeInput;
    private readonly SyncRef<TextInput> _valueInput;
    private readonly SyncRef<Button> _holdButton;
    private readonly SyncRef<Button> _straightButton;
    private readonly SyncRef<Button> _smoothButton;
    private readonly SyncRef<Button> _snapButton;
    private readonly SyncRef<Button> _soloButton;
    private readonly SyncRef<Button> _previewButton;

    private const float PanelWidth = 1520f;
    private const float PanelHeight = 940f;
    private const float RailWidth = 300f;
    private const float GutterWidth = 74f;
    private const int TimeLabelCount = 14;
    private const int ValueLabelCount = 10;
    private const int MaxTrackRows = 400;
    private const float ZoomStep = 1.25f;
    private const float KeyMatchPixels = 6f;

    private static readonly color ButtonFill = new(0.20f, 0.19f, 0.30f, 1f);
    private static readonly color GraphBackdrop = new(0.055f, 0.055f, 0.08f, 0.97f);
    private static readonly color GroupColor = new(0.62f, 0.60f, 0.74f, 1f);
    private static readonly color ValueColor = new(0.72f, 0.56f, 1f, 1f);
    private static readonly color AlphaColor = new(0.86f, 0.86f, 0.92f, 1f);

    public static readonly LocaleText MoveKeyStep = LocaleText.Keyed("Curves.Undo.Move", "Move key");
    public static readonly LocaleText AddKeyStep = LocaleText.Keyed("Curves.Undo.Add", "Add key");
    public static readonly LocaleText DeleteKeyStep = LocaleText.Keyed("Curves.Undo.Delete", "Delete key");
    public static readonly LocaleText ShapeStep = LocaleText.Keyed("Curves.Undo.Shape", "Key shape");
    public static readonly LocaleText TangentStep = LocaleText.Keyed("Curves.Undo.Tangent", "Key tangent");
    public static readonly LocaleText TypeStep = LocaleText.Keyed("Curves.Undo.Type", "Edit key");

    // Per machine: the source this machine draws from, and the keys of the selected track.
    private ICurveSource? _source;
    private Component? _sourceTarget;
    private string? _sourceMember;
    private List<CurveKeyRow> _rows = new();
    private List<CurveKeyRow>? _gestureBefore;
    private ICurveSampler _sampler = EmptySampler.Instance;
    private long _sourceVersion = long.MinValue;
    private int _loadedTrack = -1;
    private int _localVersion;
    private bool _gesture;
    private bool _fitPending = true;
    private bool _openedHere;

    // Authority only: what the synced UI last showed.
    private readonly Dictionary<int, Button> _trackButtons = new();
    private string _trackListSignature = "";
    private int _highlightedTrack = -1;
    private string _chipSignature = "";
    private string _axisSignature = "";
    private string _keySignature = "";
    private string _toolSignature = "";
    private string _statusSignature = "";
    private string _titleSignature = "";

    // Per machine, for preview scrubbing.
    private readonly List<Animator> _previewAnimators = new();
    private double _previewScanTime = double.NegativeInfinity;

    public CurveStudioPanel()
    {
        Target = new SyncRef<Component>(this);
        Member = new Sync<string>(this, "");
        Clip = new AssetRef<AnimationAsset>(this);
        Track = new Sync<int>(this, 0);
        Channel = new Sync<int>(this, 0);
        Key = new Sync<int>(this, -1);
        Solo = new Sync<bool>(this, false);
        Snap = new Sync<bool>(this, true);
        Preview = new Sync<bool>(this, false);
        TimeView = new Sync<float2>(this, new float2(0f, 1f));
        ValueView = new Sync<float2>(this, new float2(-1f, 1f));
        Playhead = new Sync<float>(this, 0f);
        Status = new Sync<string>(this, "");
        _trackHost = new SyncRef<Slot>(this);
        _chipHost = new SyncRef<Slot>(this);
        _timeAxis = new SyncRef<Slot>(this);
        _valueAxis = new SyncRef<Slot>(this);
        _statusText = new SyncRef<Text>(this);
        _trackLabel = new SyncRef<Text>(this);
        _keyLabel = new SyncRef<Text>(this);
        _timeInput = new SyncRef<TextInput>(this);
        _valueInput = new SyncRef<TextInput>(this);
        _holdButton = new SyncRef<Button>(this);
        _straightButton = new SyncRef<Button>(this);
        _smoothButton = new SyncRef<Button>(this);
        _snapButton = new SyncRef<Button>(this);
        _soloButton = new SyncRef<Button>(this);
        _previewButton = new SyncRef<Button>(this);
    }

    // WHAT THE GRAPH READS

    public ICurveSource? Source => _source;

    public IReadOnlyList<CurveKeyRow> Rows => _rows;

    public ICurveSampler Sampler => _sampler;

    // Bumps whenever something the graph draws changes on this machine.
    public int LocalVersion => _localVersion;

    public CurveTrackInfo? Info
    {
        get
        {
            var tracks = _source?.Tracks;
            if (tracks == null || tracks.Count == 0)
                return null;
            return tracks[ClampTrack(Track.Value)];
        }
    }

    public bool CanEditKeys => _source?.CanEdit == true && Info?.Editable == true;

    public bool ChannelVisible(int channel) => !Solo.Value || channel == Channel.Value;

    public void Touch() => _localVersion++;

    // SPAWN

    // One panel per curve: asking again for the same one returns it where it is.
    public static CurveStudioPanel? Open(Component target, string? member, float3 near)
    {
        var world = target?.World;
        if (target == null || target.IsDestroyed || world?.RootSlot == null)
            return null;
        string wanted = member ?? "";
        foreach (var child in world.RootSlot.Children)
        {
            var existing = child.GetComponent<CurveStudioPanel>();
            if (existing != null && !existing.IsDestroyed && ReferenceEquals(existing.Target.Target, target)
                && string.Equals(existing.Member.Value ?? "", wanted, StringComparison.Ordinal))
                return existing;
        }

        var head = world.LocalUser?.Root?.HeadSlot;
        var position = near;
        if (head != null)
        {
            var toward = head.GlobalPosition - near;
            if (toward.LengthSquared > 1e-6f)
                position = near + toward.Normalized * 0.25f + float3.Up * 0.05f;
        }

        var slot = world.RootSlot.AddSlot("Curve Studio");
        slot.Persistent.Value = false;
        slot.Tag.Value = "Developer";
        slot.GlobalPosition = position;
        if (head != null)
        {
            var toViewer = head.GlobalPosition - position;
            toViewer.y = 0f;
            if (toViewer.LengthSquared > 1e-6f)
                slot.GlobalRotation = floatQ.AxisAngleRad(float3.Up, MathF.Atan2(toViewer.x, toViewer.z));
        }
        slot.LocalScale.Value = float3.One * InspectorUI.PanelScale;

        var panel = slot.AttachComponent<CurveStudioPanel>();
        panel._openedHere = true;
        panel.Target.Target = target;
        panel.Member.Value = wanted;
        if (target is AnimationProvider provider)
            panel.Clip.Target = provider;
        return panel;
    }

    public override void OnAttach()
    {
        base.OnAttach();
        var theme = Slot.GetOrAttachComponent<UITheme>();
        var shell = Slot.GetOrAttachComponent<PanelShell>();
        shell.Title.Value = "Curves";
        shell.Size.Value = new float2(PanelWidth, PanelHeight);
        theme.ApplyTo(shell);
        shell.RebuildContent(BuildLayout);
    }

    public override void OnDestroy()
    {
        _source?.Dispose();
        _source = null;
        base.OnDestroy();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        _localVersion++;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (World?.IsAuthority == true && Target.RawTarget is { IsDestroyed: true })
        {
            Slot.Destroy();
            return;
        }
        EnsureSource();
        PollSource();
        if (World?.IsAuthority == true)
            RefreshUi();
    }

    // SOURCE

    private void EnsureSource()
    {
        var target = Target.Target;
        string member = Member.Value ?? "";
        if (_source != null && ReferenceEquals(target, _sourceTarget) && member == _sourceMember)
            return;
        _source?.Dispose();
        _source = target != null ? CurveSources.Create(target, member, this) : null;
        _sourceTarget = target;
        _sourceMember = member;
        _sourceVersion = long.MinValue;
        _loadedTrack = -1;
        _rows = new List<CurveKeyRow>();
        _sampler = EmptySampler.Instance;
        _fitPending = true;
        _localVersion++;
    }

    private void PollSource()
    {
        var source = _source;
        if (source == null || !source.IsReady || _gesture)
            return;
        int track = ClampTrack(Track.Value);
        long version = source.Version;
        if (version == _sourceVersion && track == _loadedTrack)
            return;
        bool trackChanged = track != _loadedTrack;
        _sourceVersion = version;
        _loadedTrack = track;
        _rows = source.ReadKeys(track);
        _sampler = source.CreateSampler(track, _rows);
        _localVersion++;

        // The machine that opened the panel frames the first track it sees, so two machines never fight
        // over the opening view; after that a view only moves when somebody moves it.
        if (_fitPending && trackChanged && _openedHere)
        {
            _fitPending = false;
            Channel.Value = BusiestChannel();
            FitView();
        }
    }

    private int ClampTrack(int track)
    {
        int count = _source?.Tracks.Count ?? 0;
        if (count == 0)
            return 0;
        return System.Math.Clamp(track, 0, count - 1);
    }

    private void Report(string message)
    {
        if (!IsDestroyed && Status.Value != message)
            Status.Value = message;
    }

    // GESTURES (the pressing machine)

    public void BeginGesture()
    {
        _gesture = true;
        _gestureBefore = CurveKeyRow.CopyAll(_rows);
    }

    public void UpdateGesture(List<CurveKeyRow> rows)
    {
        if (_source == null)
            return;
        int track = ClampTrack(Track.Value);
        _rows = rows;
        _sampler = _source.CreateSampler(track, rows);
        _localVersion++;
        _source.Preview(track, rows);
    }

    public void EndGesture(LocaleText step)
    {
        if (!_gesture)
            return;
        _gesture = false;
        var before = _gestureBefore;
        _gestureBefore = null;
        if (_source == null || before == null || CurveKeyRow.Same(before, _rows))
            return;
        _source.Commit(ClampTrack(Track.Value), before, _rows, step, Report);
        _sourceVersion = _source.Version;
    }

    public bool GestureActive => _gesture;

    // A whole edit in one go: a button, a typed number.
    public void ApplyEdit(List<CurveKeyRow> rows, LocaleText step)
    {
        if (!CanEditKeys)
        {
            Report(_source?.ReadOnlyReason ?? "");
            return;
        }
        BeginGesture();
        UpdateGesture(rows);
        EndGesture(step);
    }

    public List<CurveKeyRow> CopyRows() => CurveKeyRow.CopyAll(_rows);

    public float SnapTime(float time)
    {
        float step = _source?.SnapStep ?? 0f;
        if (Snap.Value && step > 0f)
            time = MathF.Round(time / step) * step;
        return ClampTime(time);
    }

    public float ClampTime(float time)
    {
        if (_source?.FixedTimeRange == true)
            return System.Math.Clamp(time, 0f, 1f);
        return time < 0f ? 0f : time;
    }

    public float ShapeValue(float value)
    {
        var info = Info;
        if (info == null)
            return value;
        if (info.HoldOnly && info.Whole && info.Channels.Length == 1 && info.Channels[0] == "On")
            return value >= 0.5f ? 1f : 0f;
        return info.Whole ? MathF.Round(value) : value;
    }

    // Inserts a key at this time (the curve's own value on every channel, then the given channel moved
    // to the given value). A key already sitting on that time is reused instead of stacked. Returns its
    // index in rows.
    public int InsertKey(List<CurveKeyRow> rows, float time, int channel, float? value)
    {
        var info = Info;
        int channels = info?.Channels.Length ?? 0;
        time = SnapTime(time);

        float tolerance = System.Math.Max(1e-4f, (_source?.SnapStep ?? 0.01f) * 0.25f);
        for (int i = 0; i < rows.Count; i++)
        {
            if (MathF.Abs(rows[i].Time - time) <= tolerance)
            {
                if (value.HasValue && channel >= 0 && channel < channels)
                    SetChannel(rows[i], channel, ShapeValue(value.Value));
                return i;
            }
        }

        var sampler = _source?.CreateSampler(ClampTrack(Track.Value), rows) ?? (ICurveSampler)EmptySampler.Instance;
        var row = sampler.RowAt(time);
        if (row.Channels != channels)
            row = new CurveKeyRow(channels) { Time = time };
        row.Time = time;
        int index = CurveKeyRow.InsertIndex(rows, time);
        var neighbour = index > 0 ? rows[index - 1] : (index < rows.Count ? rows[index] : null);
        row.Shape = info == null ? KeyframeInterpolation.Linear
            : info.HoldOnly ? KeyframeInterpolation.Step
            : neighbour?.Shape ?? KeyframeInterpolation.Linear;
        if (!info?.AllowHold == true && row.Shape == KeyframeInterpolation.Step)
            row.Shape = KeyframeInterpolation.Linear;
        if (!info?.AllowSmooth == true && row.Shape == KeyframeInterpolation.Cubic)
            row.Shape = KeyframeInterpolation.Linear;
        if (value.HasValue && channel >= 0 && channel < channels)
            SetChannel(row, channel, ShapeValue(value.Value));
        rows.Insert(index, row);
        if (row.Shape == KeyframeInterpolation.Cubic)
            AutoTangents(rows, index);
        return index;
    }

    public static void SetChannel(CurveKeyRow row, int channel, float value)
    {
        row.Values[channel] = value;
        row.RotationEdited = true;
    }

    // Catmull-Rom slopes: through the neighbours, one-sided at the ends.
    public static void AutoTangents(List<CurveKeyRow> rows, int index)
    {
        var row = rows[index];
        var prev = index > 0 ? rows[index - 1] : null;
        var next = index < rows.Count - 1 ? rows[index + 1] : null;
        for (int c = 0; c < row.Channels; c++)
        {
            float slope = 0f;
            if (prev != null && next != null && next.Time - prev.Time > 1e-6f)
                slope = (next.Values[c] - prev.Values[c]) / (next.Time - prev.Time);
            else if (next != null && next.Time - row.Time > 1e-6f)
                slope = (next.Values[c] - row.Values[c]) / (next.Time - row.Time);
            else if (prev != null && row.Time - prev.Time > 1e-6f)
                slope = (row.Values[c] - prev.Values[c]) / (row.Time - prev.Time);
            row.In[c] = slope;
            row.Out[c] = slope;
        }
    }

    // VIEW

    // Frames every key and the curve between them on the channels being shown, plus the clip's length.
    public void FitView()
    {
        var source = _source;
        if (source == null)
            return;
        float t0 = 0f;
        float t1 = source.Duration;
        if (_rows.Count > 0)
        {
            t0 = MathF.Min(t0, _rows[0].Time);
            t1 = MathF.Max(t1, _rows[^1].Time);
        }
        if (source.FixedTimeRange)
        {
            t0 = 0f;
            t1 = 1f;
        }
        if (t1 - t0 < 1e-3f)
            t1 = t0 + 1f;
        float timePad = (t1 - t0) * 0.04f;

        float v0 = float.PositiveInfinity;
        float v1 = float.NegativeInfinity;
        int channels = Info?.Channels.Length ?? 0;
        for (int c = 0; c < channels; c++)
        {
            if (!ChannelVisible(c))
                continue;
            float carry = SeedCarry(c, t0);
            for (int i = 0; i <= 64; i++)
            {
                float v = _sampler.Sample(c, t0 + (t1 - t0) * i / 64f, ref carry);
                if (!float.IsFinite(v))
                    continue;
                v0 = MathF.Min(v0, v);
                v1 = MathF.Max(v1, v);
            }
            foreach (var row in _rows)
            {
                v0 = MathF.Min(v0, row.Values[c]);
                v1 = MathF.Max(v1, row.Values[c]);
            }
        }
        if (!float.IsFinite(v0) || !float.IsFinite(v1))
        {
            v0 = 0f;
            v1 = 1f;
        }
        if (v1 - v0 < 1e-3f)
        {
            float mid = (v0 + v1) * 0.5f;
            float half = MathF.Max(0.5f, MathF.Abs(mid) * 0.25f);
            v0 = mid - half;
            v1 = mid + half;
        }
        float valuePad = (v1 - v0) * 0.12f;
        SetView(new float2(t0 - timePad, t1 + timePad), new float2(v0 - valuePad, v1 + valuePad));
    }

    // The key at or before this time on that channel, so a rotation's samples start on the same turn its
    // keys are drawn on.
    public float SeedCarry(int channel, float time)
    {
        if (_rows.Count == 0 || channel < 0 || channel >= _rows[0].Channels)
            return float.NaN;
        var seed = _rows[0];
        foreach (var row in _rows)
        {
            if (row.Time > time)
                break;
            seed = row;
        }
        return seed.Values[channel];
    }

    public void SetView(float2 time, float2 value)
    {
        if (!(time.y > time.x) || !(value.y > value.x) || !float.IsFinite(time.x + time.y + value.x + value.y))
            return;
        if (TimeView.Value != time)
            TimeView.Value = time;
        if (ValueView.Value != value)
            ValueView.Value = value;
    }

    private void Zoom(float timeFactor, float valueFactor)
    {
        var t = TimeView.Value;
        var v = ValueView.Value;
        float tc = (t.x + t.y) * 0.5f;
        float vc = (v.x + v.y) * 0.5f;
        float th = (t.y - t.x) * 0.5f * timeFactor;
        float vh = (v.y - v.x) * 0.5f * valueFactor;
        SetView(new float2(tc - th, tc + th), new float2(vc - vh, vc + vh));
    }

    // PREVIEW

    // Scrubbing moves every animator playing this clip, wherever it is, so the model follows the ruler.
    public void ScrubTo(float time)
    {
        time = ClampTime(time);
        if (Playhead.Value != time)
            Playhead.Value = time;
        if (!Preview.Value)
            return;
        foreach (var animator in PreviewAnimators())
            animator.Position = time;
    }

    private List<Animator> PreviewAnimators()
    {
        double now = World?.Time?.TotalTime ?? 0.0;
        if (now - _previewScanTime < 2.0)
        {
            _previewAnimators.RemoveAll(a => a == null || a.IsDestroyed);
            return _previewAnimators;
        }
        _previewScanTime = now;
        _previewAnimators.Clear();
        if (Target.Target is AnimationProvider provider && World?.RootSlot != null)
        {
            foreach (var animator in World.RootSlot.GetComponentsInChildren<Animator>())
            {
                if (ReferenceEquals(animator.Clip.Target, provider))
                    _previewAnimators.Add(animator);
            }
        }
        return _previewAnimators;
    }

    // ACTIONS (the pressing machine)

    public void HandleInspectorAction(string argument, UIInteractionContext context)
    {
        if (string.IsNullOrEmpty(argument) || _source == null)
            return;

        if (argument.StartsWith("track:", StringComparison.Ordinal) && int.TryParse(argument[6..], out int track))
        {
            SelectTrack(track);
            return;
        }
        if (argument.StartsWith("chan:", StringComparison.Ordinal) && int.TryParse(argument[5..], out int channel))
        {
            Channel.Value = channel;
            return;
        }
        if (argument.StartsWith("shape:", StringComparison.Ordinal) && int.TryParse(argument[6..], out int shape))
        {
            SetShape((KeyframeInterpolation)shape);
            return;
        }

        switch (argument)
        {
            case "fit":
                FitView();
                break;
            case "time-":
                Zoom(ZoomStep, 1f);
                break;
            case "time+":
                Zoom(1f / ZoomStep, 1f);
                break;
            case "value-":
                Zoom(1f, ZoomStep);
                break;
            case "value+":
                Zoom(1f, 1f / ZoomStep);
                break;
            case "snap":
                Snap.Value = !Snap.Value;
                break;
            case "solo":
                Solo.Value = !Solo.Value;
                break;
            case "preview":
                TogglePreview();
                break;
            case "prev":
                StepKey(-1);
                break;
            case "next":
                StepKey(1);
                break;
            case "add":
                AddAtPlayhead();
                break;
            case "delete":
                DeleteSelected();
                break;
            case "flat":
                Flatten();
                break;
        }
    }

    private void SelectTrack(int track)
    {
        if (_source == null)
            return;
        Track.Value = ClampTrack(track);
        Key.Value = -1;
        PollSource();
        Channel.Value = BusiestChannel();
        FitView();
    }

    // The channel that actually moves: a bone turning about Y should open on Y, not on a flat X.
    private int BusiestChannel()
    {
        int channels = Info?.Channels.Length ?? 0;
        int best = 0;
        float bestRange = -1f;
        for (int c = 0; c < channels; c++)
        {
            float lo = float.PositiveInfinity;
            float hi = float.NegativeInfinity;
            foreach (var row in _rows)
            {
                lo = MathF.Min(lo, row.Values[c]);
                hi = MathF.Max(hi, row.Values[c]);
            }
            float range = hi - lo;
            if (range > bestRange + 1e-6f)
            {
                bestRange = range;
                best = c;
            }
        }
        return best;
    }

    private void StepKey(int direction)
    {
        if (_rows.Count == 0)
            return;
        int key = Key.Value;
        if (key < 0 || key >= _rows.Count)
        {
            // Nothing selected: start from the key nearest the playhead.
            key = direction > 0 ? CurveKeyRow.InsertIndex(_rows, Playhead.Value) : CurveKeyRow.InsertIndex(_rows, Playhead.Value) - 1;
            Key.Value = System.Math.Clamp(key, 0, _rows.Count - 1);
        }
        else
        {
            Key.Value = System.Math.Clamp(key + direction, 0, _rows.Count - 1);
        }
        if (Key.Value >= 0 && Key.Value < _rows.Count)
            ScrubTo(_rows[Key.Value].Time);
    }

    private void AddAtPlayhead()
    {
        if (!CanEditKeys)
        {
            Report(_source?.ReadOnlyReason ?? "");
            return;
        }
        var rows = CopyRows();
        float time = Playhead.Value;
        var view = TimeView.Value;
        if (time < view.x || time > view.y)
            time = (view.x + view.y) * 0.5f;
        int index = InsertKey(rows, time, -1, null);
        ApplyEdit(rows, AddKeyStep);
        Key.Value = index;
    }

    private void DeleteSelected()
    {
        int key = Key.Value;
        if (!CanEditKeys || key < 0 || key >= _rows.Count)
            return;
        var rows = CopyRows();
        rows.RemoveAt(key);
        ApplyEdit(rows, DeleteKeyStep);
        Key.Value = rows.Count == 0 ? -1 : System.Math.Min(key, rows.Count - 1);
    }

    private void SetShape(KeyframeInterpolation shape)
    {
        int key = Key.Value;
        var info = Info;
        if (!CanEditKeys || info == null || key < 0 || key >= _rows.Count)
            return;
        if (shape == KeyframeInterpolation.Step && !info.AllowHold)
            return;
        if (shape == KeyframeInterpolation.Cubic && !info.AllowSmooth)
        {
            Report(info.Rotation ? "rotation keys blend straight or hold" : "this curve only blends straight");
            return;
        }
        if (shape != KeyframeInterpolation.Step && info.HoldOnly)
            return;
        var rows = CopyRows();
        bool wasCubic = rows[key].Shape == KeyframeInterpolation.Cubic;
        rows[key].Shape = shape;
        if (shape == KeyframeInterpolation.Cubic && !wasCubic)
            AutoTangents(rows, key);
        ApplyEdit(rows, ShapeStep);
    }

    private void Flatten()
    {
        int key = Key.Value;
        var info = Info;
        if (!CanEditKeys || info == null || !info.AllowSmooth || key < 0 || key >= _rows.Count)
            return;
        var rows = CopyRows();
        var row = rows[key];
        row.Shape = KeyframeInterpolation.Cubic;
        for (int c = 0; c < row.Channels; c++)
        {
            row.In[c] = 0f;
            row.Out[c] = 0f;
        }
        // The segment arriving at a flattened key eases in too.
        if (key > 0 && rows[key - 1].Shape == KeyframeInterpolation.Linear)
        {
            rows[key - 1].Shape = KeyframeInterpolation.Cubic;
            AutoTangents(rows, key - 1);
        }
        ApplyEdit(rows, TangentStep);
    }

    private void TogglePreview()
    {
        bool on = !Preview.Value;
        Preview.Value = on;
        _previewScanTime = double.NegativeInfinity;
        if (!on)
            return;
        foreach (var animator in PreviewAnimators())
        {
            if (animator.Playback.IsPlaying)
                animator.Pause();
            animator.Position = Playhead.Value;
        }
        if (_previewAnimators.Count == 0)
            Report(Target.Target is AnimationProvider ? "no animator plays this clip" : "preview moves animators playing a clip");
    }

    [SyncMethod]
    public void OnTimeSubmitted(TextInput input, string text)
    {
        int key = Key.Value;
        if (!CanEditKeys || key < 0 || key >= _rows.Count || !TryParse(text, out float time))
        {
            _keySignature = "";
            return;
        }
        var rows = CopyRows();
        var row = rows[key];
        rows.RemoveAt(key);
        row.Time = ClampTime(time);
        int index = CurveKeyRow.InsertIndex(rows, row.Time);
        rows.Insert(index, row);
        ApplyEdit(rows, TypeStep);
        Key.Value = index;
    }

    [SyncMethod]
    public void OnValueSubmitted(TextInput input, string text)
    {
        int key = Key.Value;
        int channel = Channel.Value;
        var info = Info;
        if (!CanEditKeys || info == null || key < 0 || key >= _rows.Count || channel < 0 || channel >= info.Channels.Length
            || !TryParse(text, out float value))
        {
            _keySignature = "";
            return;
        }
        var rows = CopyRows();
        SetChannel(rows[key], channel, ShapeValue(value));
        ApplyEdit(rows, TypeStep);
    }

    private static bool TryParse(string text, out float value)
        => float.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);

    // LAYOUT

    private void BuildLayout(UIBuilder ui)
    {
        var page = ui.Current;
        InspectorUI.ApplyTheme(ui, Slot);

        var layout = page.AttachComponent<VerticalLayout>();
        layout.Spacing.Value = 8f;
        layout.PaddingLeft.Value = 10f;
        layout.PaddingRight.Value = 10f;
        layout.PaddingTop.Value = 10f;
        layout.PaddingBottom.Value = 10f;
        layout.ForceExpandWidth.Value = true;
        layout.ForceExpandHeight.Value = false;

        BuildToolbar(page);
        BuildBody(page);
        BuildKeyStrip(page);
    }

    private void BuildToolbar(Slot page)
    {
        InspectorUI.FixedRow(page, "Toolbar", 40f, out var bar, Slot);
        Tool(bar, "fit", "Fit", 64f);
        Tool(bar, "time-", "Time −", 84f);
        Tool(bar, "time+", "Time +", 84f);
        Tool(bar, "value-", "Value −", 90f);
        Tool(bar, "value+", "Value +", 90f);
        _snapButton.Target = Tool(bar, "snap", "Snap", 78f);
        _soloButton.Target = Tool(bar, "solo", "Solo", 70f);
        _previewButton.Target = Tool(bar, "preview", "Preview", 96f);

        bar.PushStyle();
        bar.FlexibleWidth(1f);
        var status = bar.Text("", InspectorUI.FontSize - 1f, InspectorUI.MutedColor);
        InspectorUI.FillParent(status.RectTransform!);
        status.HorizontalAlignment.Value = TextHorizontalAlignment.Right;
        status.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        status.WordWrap.Value = false;
        status.ClipToRect.Value = true;
        bar.PopStyle();
        _statusText.Target = status;
    }

    private Button Tool(UIBuilder ui, string action, string label, float width)
    {
        ui.PushStyle();
        ui.BackgroundColor(ButtonFill);
        var button = InspectorUI.RelayButton(ui, this, action, label, width);
        ui.PopStyle();
        return button;
    }

    private void BuildBody(Slot page)
    {
        var body = page.AddSlot("Body");
        body.AttachComponent<RectTransform>();
        var bodyLE = body.AttachComponent<LayoutElement>();
        bodyLE.FlexibleHeight.Value = 1f;
        bodyLE.MinHeight.Value = 300f;
        var bodyLayout = body.AttachComponent<HorizontalLayout>();
        bodyLayout.Spacing.Value = 8f;
        bodyLayout.ForceExpandHeight.Value = true;
        bodyLayout.ForceExpandWidth.Value = false;

        BuildRail(body);
        BuildGraphColumn(body);
    }

    private void BuildRail(Slot body)
    {
        var rail = body.AddSlot("Tracks");
        rail.AttachComponent<RectTransform>();
        var le = rail.AttachComponent<LayoutElement>();
        le.MinWidth.Value = RailWidth;
        le.PreferredWidth.Value = RailWidth;
        le.FlexibleWidth.Value = 0f;
        var layout = rail.AttachComponent<VerticalLayout>();
        layout.Spacing.Value = 6f;
        layout.ForceExpandWidth.Value = true;
        layout.ForceExpandHeight.Value = false;

        InspectorUI.SectionHeader(rail, "TRACKS", Slot);

        var host = rail.AddSlot("List");
        host.AttachComponent<RectTransform>();
        var hostLE = host.AttachComponent<LayoutElement>();
        hostLE.FlexibleHeight.Value = 1f;
        hostLE.MinHeight.Value = 120f;

        var scrollUi = new UIBuilder(host);
        InspectorUI.ApplyTheme(scrollUi, Slot);
        var scroll = scrollUi.ScrollRect(out var content, null, InspectorUI.PaneColor);
        scroll.RowsActOnRelease.Value = true;
        InspectorUI.FillParent(scroll.Slot.GetComponent<RectTransform>()!);
        var contentLayout = content.Slot.AttachComponent<VerticalLayout>();
        contentLayout.Spacing.Value = 3f;
        contentLayout.PaddingLeft.Value = 4f;
        contentLayout.PaddingRight.Value = 4f;
        contentLayout.PaddingTop.Value = 4f;
        contentLayout.PaddingBottom.Value = 4f;
        contentLayout.ForceExpandWidth.Value = true;
        contentLayout.ForceExpandHeight.Value = false;
        _trackHost.Target = content.Slot;
    }

    private void BuildGraphColumn(Slot body)
    {
        var column = body.AddSlot("Graph Column");
        column.AttachComponent<RectTransform>();
        var le = column.AttachComponent<LayoutElement>();
        le.FlexibleWidth.Value = 1f;
        var layout = column.AttachComponent<VerticalLayout>();
        layout.Spacing.Value = 6f;
        layout.ForceExpandWidth.Value = true;
        layout.ForceExpandHeight.Value = false;

        var chips = InspectorUI.FixedRow(column, "Channels", 34f, out var chipUi, Slot);
        _chipHost.Target = chips;
        chipUi.PushStyle();
        chipUi.FlexibleWidth(1f);
        var trackLabel = chipUi.Text("", InspectorUI.FontSize, InspectorUI.TextColor);
        InspectorUI.FillParent(trackLabel.RectTransform!);
        trackLabel.HorizontalAlignment.Value = TextHorizontalAlignment.Right;
        trackLabel.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        trackLabel.WordWrap.Value = false;
        trackLabel.ClipToRect.Value = true;
        chipUi.PopStyle();
        _trackLabel.Target = trackLabel;

        var block = column.AddSlot("Graph Block");
        block.AttachComponent<RectTransform>();
        var blockLE = block.AttachComponent<LayoutElement>();
        blockLE.FlexibleHeight.Value = 1f;
        blockLE.MinHeight.Value = 240f;

        var area = block.AddSlot("Graph");
        var areaRect = area.AttachComponent<RectTransform>();
        areaRect.AnchorMin.Value = float2.Zero;
        areaRect.AnchorMax.Value = float2.One;
        areaRect.OffsetMin.Value = new float2(GutterWidth, 0f);
        areaRect.OffsetMax.Value = float2.Zero;
        var backdrop = area.AttachComponent<Image>();
        backdrop.Tint.Value = GraphBackdrop;
        backdrop.CornerRadius.Value = DashTheme.RadiusControl;
        var mask = area.AttachComponent<Mask>();
        mask.ShowMaskGraphic.Value = true;

        var plot = area.AddSlot("Plot");
        InspectorUI.FillParent(plot.AttachComponent<RectTransform>());
        plot.AttachComponent<GraphicChunkRoot>();
        var graphic = plot.AttachComponent<CurveGraphGraphic>();
        var graph = plot.AttachComponent<CurveGraph>();
        graph.Panel.Target = this;
        graphic.Panel.Target = this;
        graphic.Graph.Target = graph;

        var valueAxis = block.AddSlot("Value Axis");
        var valueRect = valueAxis.AttachComponent<RectTransform>();
        valueRect.AnchorMin.Value = float2.Zero;
        valueRect.AnchorMax.Value = new float2(0f, 1f);
        valueRect.OffsetMin.Value = new float2(0f, CurveView.RulerHeight);
        valueRect.OffsetMax.Value = new float2(GutterWidth - 6f, 0f);
        valueAxis.AttachComponent<GraphicChunkRoot>();
        BuildLabels(valueAxis, ValueLabelCount, TextHorizontalAlignment.Right);
        _valueAxis.Target = valueAxis;

        var timeAxis = block.AddSlot("Time Axis");
        var timeRect = timeAxis.AttachComponent<RectTransform>();
        timeRect.AnchorMin.Value = float2.Zero;
        timeRect.AnchorMax.Value = new float2(1f, 0f);
        timeRect.OffsetMin.Value = new float2(GutterWidth, 0f);
        timeRect.OffsetMax.Value = new float2(0f, CurveView.RulerHeight);
        timeAxis.AttachComponent<GraphicChunkRoot>();
        BuildLabels(timeAxis, TimeLabelCount, TextHorizontalAlignment.Center);
        _timeAxis.Target = timeAxis;
    }

    private void BuildLabels(Slot axis, int count, TextHorizontalAlignment alignment)
    {
        var ui = new UIBuilder(axis);
        InspectorUI.ApplyTheme(ui, Slot);
        for (int i = 0; i < count; i++)
        {
            var text = ui.Text("", InspectorUI.FontSize - 3f, InspectorUI.MutedColor);
            text.HorizontalAlignment.Value = alignment;
            text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
            text.WordWrap.Value = false;
            text.Slot.ActiveSelf.Value = false;
        }
    }

    private void BuildKeyStrip(Slot page)
    {
        InspectorUI.FixedRow(page, "Key", 42f, out var strip, Slot);

        Caption(strip, "KEY", 46f, InspectorUI.CyanColor);
        Tool(strip, "prev", "<", 40f);
        Tool(strip, "next", ">", 40f);
        Caption(strip, "Time", 48f, InspectorUI.MutedColor);
        _timeInput.Target = Input(strip, "Time Input", 110f, OnTimeSubmitted);
        Caption(strip, "Value", 54f, InspectorUI.MutedColor);
        _valueInput.Target = Input(strip, "Value Input", 124f, OnValueSubmitted);
        _holdButton.Target = Tool(strip, "shape:0", "Hold", 66f);
        _straightButton.Target = Tool(strip, "shape:1", "Straight", 92f);
        _smoothButton.Target = Tool(strip, "shape:2", "Smooth", 86f);
        Tool(strip, "flat", "Flat", 60f);
        Tool(strip, "add", "Add key", 86f);

        strip.PushStyle();
        strip.BackgroundColor(InspectorUI.DangerColor);
        InspectorUI.RelayButton(strip, this, "delete", "Delete", 76f);
        strip.PopStyle();

        strip.PushStyle();
        strip.FlexibleWidth(1f);
        var keyLabel = strip.Text("", InspectorUI.FontSize - 1f, InspectorUI.MutedColor);
        InspectorUI.FillParent(keyLabel.RectTransform!);
        keyLabel.HorizontalAlignment.Value = TextHorizontalAlignment.Right;
        keyLabel.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        keyLabel.WordWrap.Value = false;
        keyLabel.ClipToRect.Value = true;
        strip.PopStyle();
        _keyLabel.Target = keyLabel;
    }

    private static void Caption(UIBuilder ui, string label, float width, color tint)
    {
        ui.PushStyle();
        ui.MinWidth(width);
        ui.PreferredWidth(width);
        ui.FlexibleWidth(0f);
        var text = ui.Text(label, InspectorUI.FontSize - 1f, tint);
        InspectorUI.FillParent(text.RectTransform!);
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Right;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ui.PopStyle();
    }

    private static TextInput Input(UIBuilder ui, string name, float width, Action<TextInput, string> submit)
    {
        ui.PushStyle();
        ui.MinWidth(width);
        ui.PreferredWidth(width);
        ui.FlexibleWidth(0f);
        var input = InspectorUI.CreateTextInput(ui, name, TextHorizontalAlignment.Center);
        input.SubmitOnBlur.Value = true;
        input.SetSubmitAction(submit);
        ui.PopStyle();
        return input;
    }

    // SYNCED UI (authority)

    private void RefreshUi()
    {
        RefreshTitle();
        RefreshTrackList();
        RefreshChips();
        RefreshAxes();
        RefreshKeyStrip();
        RefreshTools();
        RefreshStatus();
    }

    private void RefreshTitle()
    {
        string title = _source == null ? "Curves" : $"Curves · {_source.Title}";
        if (title == _titleSignature)
            return;
        _titleSignature = title;
        var shell = Slot.GetComponent<PanelShell>();
        if (shell != null && shell.Title.Value != title)
            shell.Title.Value = title;
    }

    private void RefreshTrackList()
    {
        var host = _trackHost.Target;
        if (host == null || host.IsDestroyed)
            return;
        var tracks = _source?.Tracks;
        string signature = tracks == null ? "none" : TrackSignature(tracks);
        if (signature != _trackListSignature)
        {
            _trackListSignature = signature;
            _highlightedTrack = -1;
            RebuildTrackList(host, tracks);
        }
        int selected = ClampTrack(Track.Value);
        if (selected != _highlightedTrack)
        {
            if (_trackButtons.TryGetValue(_highlightedTrack, out var old) && !old.IsDestroyed)
                InspectorUI.ApplyStateTint(old.Slot.GetComponent<Image>()?.Tint, ButtonFill);
            if (_trackButtons.TryGetValue(selected, out var now) && !now.IsDestroyed)
                InspectorUI.ApplyStateTint(now.Slot.GetComponent<Image>()?.Tint, InspectorUI.SelectionColor);
            _highlightedTrack = selected;
        }
    }

    private static string TrackSignature(IReadOnlyList<CurveTrackInfo> tracks)
    {
        var parts = new System.Text.StringBuilder();
        parts.Append(tracks.Count);
        foreach (var track in tracks)
            parts.Append('|').Append(track.Group).Append('.').Append(track.Name).Append(':').Append(track.Channels.Length);
        return parts.ToString();
    }

    private void RebuildTrackList(Slot host, IReadOnlyList<CurveTrackInfo>? tracks)
    {
        host.DestroyChildren();
        _trackButtons.Clear();
        if (tracks == null || tracks.Count == 0)
        {
            Note(host, _source == null ? "nothing to show" : _source.IsReady ? "no tracks" : "loading...");
            host.GetComponent<ScrollContentSizer>()?.Invalidate();
            return;
        }

        string? group = null;
        int shown = System.Math.Min(tracks.Count, MaxTrackRows);
        for (int i = 0; i < shown; i++)
        {
            var track = tracks[i];
            if (track.Group != group)
            {
                group = track.Group;
                if (!string.IsNullOrEmpty(group))
                    Note(host, group, GroupColor, TextHorizontalAlignment.Left);
            }
            InspectorUI.FixedRow(host, "Track", 32f, out var row, Slot);
            row.PushStyle();
            row.BackgroundColor(ButtonFill);
            row.TextColor(track.Editable ? InspectorUI.TextColor : InspectorUI.MutedColor);
            string channels = track.Channels.Length > 1 ? "  " + string.Join("", track.Channels) : "";
            var button = InspectorUI.RelayButton(row, this, $"track:{i}", track.Name + channels, 0f);
            var label = button.Slot.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
                label.WordWrap.Value = false;
                label.ClipToRect.Value = true;
                label.RectTransform!.OffsetMin.Value = new float2(12f, 0f);
            }
            row.PopStyle();
            _trackButtons[i] = button;
        }
        if (tracks.Count > shown)
            Note(host, $"{tracks.Count - shown} more tracks not listed");
        host.GetComponent<ScrollContentSizer>()?.Invalidate();
    }

    private void Note(Slot host, string text, color? tint = null, TextHorizontalAlignment alignment = TextHorizontalAlignment.Center)
    {
        InspectorUI.FixedRow(host, "Note", 26f, out var ui, Slot);
        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var label = ui.Text(text, InspectorUI.FontSize - 2f, tint ?? InspectorUI.MutedColor);
        InspectorUI.FillParent(label.RectTransform!);
        label.HorizontalAlignment.Value = alignment;
        label.VerticalAlignment.Value = TextVerticalAlignment.Bottom;
        label.WordWrap.Value = false;
        label.ClipToRect.Value = true;
        ui.PopStyle();
    }

    private void RefreshChips()
    {
        var host = _chipHost.Target;
        if (host == null || host.IsDestroyed)
            return;
        var info = Info;
        string signature = info == null ? "none" : $"{ClampTrack(Track.Value)}|{string.Join(",", info.Channels)}|{Channel.Value}|{Solo.Value}|{info.Label}";
        if (signature == _chipSignature)
            return;
        _chipSignature = signature;

        // The chips sit before the label; rebuild them in front of it.
        var label = _trackLabel.Target;
        foreach (var child in new List<Slot>(host.Children))
        {
            if (label == null || !ReferenceEquals(child, label.Slot))
                child.Destroy();
        }
        if (label != null && !label.IsDestroyed)
            label.Content.Value = info == null ? "" : info.Label + (info.Editable ? "" : "  (read only)");
        if (info == null)
            return;

        var ui = new UIBuilder(host);
        InspectorUI.ApplyTheme(ui, Slot);
        for (int c = 0; c < info.Channels.Length; c++)
        {
            bool active = c == Channel.Value;
            var tint = ChannelColor(info, c);
            ui.PushStyle();
            ui.BackgroundColor(active ? tint : new color(tint.r * 0.35f, tint.g * 0.35f, tint.b * 0.35f, 1f));
            ui.TextColor(active ? new color(0.05f, 0.05f, 0.08f, 1f) : InspectorUI.TextColor);
            var chip = InspectorUI.RelayButton(ui, this, $"chan:{c}", info.Channels[c], info.Channels[c].Length > 2 ? 72f : 44f);
            ui.PopStyle();
            chip.Slot.OrderOffset.Value = c - 100;
        }
    }

    public static color ChannelColor(CurveTrackInfo info, int channel)
    {
        string name = channel >= 0 && channel < info.Channels.Length ? info.Channels[channel] : "";
        return name switch
        {
            "X" or "R" => InspectorUI.AxisXColor,
            "Y" or "G" => InspectorUI.AxisYColor,
            "Z" or "B" => InspectorUI.AxisZColor,
            "W" or "A" => AlphaColor,
            _ => ValueColor,
        };
    }

    private void RefreshAxes()
    {
        var time = TimeView.Value;
        var value = ValueView.Value;
        string signature = $"{time.x}|{time.y}|{value.x}|{value.y}";
        if (signature == _axisSignature)
            return;
        _axisSignature = signature;
        FillAxis(_timeAxis.Target, time.x, time.y, TimeLabelCount - 2, vertical: false);
        FillAxis(_valueAxis.Target, value.x, value.y, ValueLabelCount - 3, vertical: true);
    }

    private static void FillAxis(Slot? axis, float min, float max, int target, bool vertical)
    {
        if (axis == null || axis.IsDestroyed || !(max > min))
            return;
        float step = CurveTicks.Step(max - min, target);
        float tick = CurveTicks.First(min, step);
        foreach (var child in axis.Children)
        {
            var text = child.GetComponent<Text>();
            var rect = child.GetComponent<RectTransform>();
            if (text == null || rect == null)
                continue;
            if (tick > max + step * 1e-3f)
            {
                if (child.ActiveSelf.Value)
                    child.ActiveSelf.Value = false;
                continue;
            }
            float f = (tick - min) / (max - min);
            string label = CurveTicks.Format(tick, step);
            if (text.Content.Value != label)
                text.Content.Value = label;
            var anchorMin = vertical ? new float2(0f, f) : new float2(f, 0f);
            var anchorMax = vertical ? new float2(1f, f) : new float2(f, 1f);
            var offsetMin = vertical ? new float2(0f, -11f) : new float2(-46f, 0f);
            var offsetMax = vertical ? new float2(0f, 11f) : new float2(46f, 0f);
            if (rect.AnchorMin.Value != anchorMin) rect.AnchorMin.Value = anchorMin;
            if (rect.AnchorMax.Value != anchorMax) rect.AnchorMax.Value = anchorMax;
            if (rect.OffsetMin.Value != offsetMin) rect.OffsetMin.Value = offsetMin;
            if (rect.OffsetMax.Value != offsetMax) rect.OffsetMax.Value = offsetMax;
            if (!child.ActiveSelf.Value)
                child.ActiveSelf.Value = true;
            tick += step;
        }
    }

    private void RefreshKeyStrip()
    {
        var info = Info;
        int key = Key.Value;
        bool has = info != null && key >= 0 && key < _rows.Count;
        int channel = info == null ? 0 : System.Math.Clamp(Channel.Value, 0, System.Math.Max(0, info.Channels.Length - 1));
        string time = has ? CurveTicks.Exact(_rows[key].Time) : "";
        string value = has && info!.Channels.Length > 0 ? CurveTicks.Exact(_rows[key].Values[channel]) : "";
        string shape = has ? ShapeName(_rows[key].Shape) : "";
        string signature = $"{time}|{value}|{shape}|{key}|{_rows.Count}|{channel}";
        if (signature == _keySignature)
            return;
        _keySignature = signature;

        SetInput(_timeInput.Target, time);
        SetInput(_valueInput.Target, value);
        var label = _keyLabel.Target;
        if (label != null && !label.IsDestroyed)
        {
            string text = has ? $"{key + 1} of {_rows.Count} · {shape}" : _rows.Count == 0 ? "no keys · press twice in the graph to add one" : $"{_rows.Count} keys · press one to select it";
            if (label.Content.Value != text)
                label.Content.Value = text;
        }
    }

    private static string ShapeName(KeyframeInterpolation shape) => shape switch
    {
        KeyframeInterpolation.Step => "hold",
        KeyframeInterpolation.Cubic => "smooth",
        _ => "straight",
    };

    private static void SetInput(TextInput? input, string text)
    {
        if (input == null || input.IsDestroyed || input.IsFocused)
            return;
        if (input.Text.Value != text)
            input.Text.Value = text;
    }

    private void RefreshTools()
    {
        var info = Info;
        int key = Key.Value;
        var shape = key >= 0 && key < _rows.Count ? _rows[key].Shape : (KeyframeInterpolation)255;
        string signature = $"{Snap.Value}|{Solo.Value}|{Preview.Value}|{shape}|{info?.AllowHold}|{info?.AllowSmooth}|{info?.HoldOnly}";
        if (signature == _toolSignature)
            return;
        _toolSignature = signature;

        Tint(_snapButton.Target, Snap.Value);
        Tint(_soloButton.Target, Solo.Value);
        Tint(_previewButton.Target, Preview.Value);
        Tint(_holdButton.Target, shape == KeyframeInterpolation.Step, info?.AllowHold != false);
        Tint(_straightButton.Target, shape == KeyframeInterpolation.Linear, info?.HoldOnly != true);
        Tint(_smoothButton.Target, shape == KeyframeInterpolation.Cubic, info?.AllowSmooth == true);
    }

    private static void Tint(Button? button, bool on, bool available = true)
    {
        if (button == null || button.IsDestroyed)
            return;
        var fill = on ? InspectorUI.AccentColor : available ? ButtonFill : new color(0.14f, 0.13f, 0.18f, 1f);
        InspectorUI.ApplyStateTint(button.Slot.GetComponent<Image>()?.Tint, fill);
        var label = button.Slot.GetComponentInChildren<Text>();
        if (label != null)
        {
            var textColor = available ? InspectorUI.TextColor : InspectorUI.BrokenColor;
            if (label.Color.Value != textColor)
                label.Color.Value = textColor;
        }
    }

    private void RefreshStatus()
    {
        string text = Status.Value ?? "";
        if (string.IsNullOrEmpty(text) && _source != null && !_source.CanEdit)
            text = _source.ReadOnlyReason;
        else if (string.IsNullOrEmpty(text) && _source == null)
            text = Target.Target == null ? "the curve is gone" : "this is not a curve";
        if (text == _statusSignature)
            return;
        _statusSignature = text;
        var label = _statusText.Target;
        if (label != null && !label.IsDestroyed)
            label.Content.Value = text;
    }

    // Hit radius in canvas units for picking a key, also used to tell a fresh press from a drag.
    public static float KeyPickRadius => 14f;

    public static float KeyMatch => KeyMatchPixels;
}
