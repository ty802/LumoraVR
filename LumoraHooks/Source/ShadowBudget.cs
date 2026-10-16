// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Godot;
using Lumora.Core;
using Lumora.Core.Components;
using Lumora.Godot.Helpers;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks;

// Hands point and spot light shadows out under the user's budget and fades each one in or out over a
// third of a second, so a handover reads as a shadow arriving rather than a pop. ShadowBudgetPlanner
// decides who; this measures the lights against the person looking and writes the result.
//
// One budget for every visible world, because they all render into the same frame. The light hooks feed
// it from their change pass and it ticks on the tree's process frame: a plan four times a second, or at
// once when a light starts or stops asking, plus a fade step for lights mid-handover. Nothing allocates
// once the candidate buffer has grown to the number of lights. Main thread. -xlinka
internal static class ShadowBudget
{
    public sealed class Entry
    {
        internal Light3D? Node;
        internal bool Wants;
        internal bool Visible;
        internal float Opacity;
        internal float Range;
        internal float Brightness;
        internal bool Tracked;
        internal bool Granted;
        internal float Fade;
        internal double GrantedAt;
        internal Light3D? AppliedNode;
        internal bool AppliedEnabled;
        internal float AppliedOpacity = -1f;
        internal int Band;
        internal uint AppliedMask;
    }

    private const double PlanInterval = 0.25;
    private const float FadeSeconds = 0.3f;

    private static readonly object _lock = new();
    private static readonly List<Entry> _entries = new();
    private static ShadowCandidate[] _candidates = new ShadowCandidate[32];
    private static bool _ticking;
    private static volatile bool _replan = true;
    private static double _lastPlan = double.NegativeInfinity;
    private static double _lastTick = double.NaN;
    private static int _loggedBudget = -1;
    private static ulong _atlasViewport;
    private static int _atlasSubdiv = -1;

    // Whether a mirror or a camera is filming the wearer's own body, and when one last did.
    private const double HeadHoldSeconds = 1.0;
    private static bool _headFilmed;
    private static double _headFilmedAt = double.NegativeInfinity;
    private static readonly List<SubViewport> _films = new();

    // The light's current wish, from its hook. Writes the shadow state straight away; a light that has
    // just started asking holds no shadow until the next plan, which this asks for.
    public static void Update(Entry entry, Light3D node, bool wants, bool visible, float opacity, float range, float brightness)
    {
        lock (_lock)
        {
            if (entry.Wants != wants || entry.Visible != visible || !ReferenceEquals(entry.Node, node))
                _replan = true;
            entry.Node = node;
            entry.Wants = wants;
            entry.Visible = visible;
            entry.Opacity = opacity;
            entry.Range = range;
            entry.Brightness = brightness;

            if (wants && !entry.Tracked)
            {
                entry.Tracked = true;
                entry.Granted = false;
                entry.Fade = 0f;
                _entries.Add(entry);
            }
            else if (!wants && entry.Tracked)
            {
                Untrack(entry);
            }
            Apply(entry);
        }

        EnsureTicking();
    }

    public static void Remove(Entry entry)
    {
        lock (_lock)
        {
            if (entry.Tracked)
                Untrack(entry);
            entry.Node = null;
            entry.AppliedNode = null;
        }
    }

    private static void Untrack(Entry entry)
    {
        _entries.Remove(entry);
        entry.Tracked = false;
        entry.Granted = false;
        entry.Fade = 0f;
        _replan = true;
    }

    // What the light shows: its authored opacity times the handover fade, and no shadow pass at all
    // while the fade sits at zero. Compared against the last write, because toggling a light's shadow
    // bumps its version and re-renders the map even when the value did not change.
    private static void Apply(Entry entry)
    {
        var node = entry.Node;
        if (node == null || !GodotObject.IsInstanceValid(node))
            return;

        bool enabled = entry.Wants && entry.Fade > 0f;
        float opacity = enabled ? entry.Opacity * entry.Fade : entry.Opacity;

        if (!ReferenceEquals(entry.AppliedNode, node))
        {
            entry.AppliedNode = node;
            entry.AppliedEnabled = !enabled;
            entry.AppliedOpacity = -1f;
            entry.AppliedMask = 0;
        }
        if (entry.AppliedEnabled != enabled)
        {
            node.ShadowEnabled = enabled;
            entry.AppliedEnabled = enabled;
        }
        if (entry.AppliedOpacity != opacity)
        {
            node.ShadowOpacity = opacity;
            entry.AppliedOpacity = opacity;
        }
        uint mask = MaskFor(entry, enabled);
        if (entry.AppliedMask != mask)
        {
            node.LightCullMask = mask;
            node.ShadowCasterMask = mask;
            entry.AppliedMask = mask;
        }
    }

    // What a light reaches. A shadowed light takes the moving-object stand-ins of its own band only
    // (MovingShadowCasters steps the two bands half a period apart), so a step re-renders half the
    // shadowed lights instead of every one of them in the same frame. A light without a shadow takes
    // neither band.
    //
    // The wearer's own body sits on LOCAL_HEAD_LAYER while its eyes draw the headless twin. The eyes do
    // not see that layer, so Godot leaves it out of their shadows as well, yet every light it sits in
    // still renders its shadow again whenever a bone moves, which in a headset is every frame. While no
    // mirror or camera films it, shadowed lights leave it out entirely: nothing anybody sees changes,
    // and the lamps around the wearer go back to their cache. -xlinka
    private static uint MaskFor(Entry entry, bool enabled)
    {
        if (!enabled)
            return ~(uint)RenderHelper.SHADOW_BANDS;
        uint mask = ~(uint)(entry.Band == 1 ? RenderHelper.SHADOW_BAND_0 : RenderHelper.SHADOW_BAND_1);
        if (!_headFilmed)
            mask &= ~(uint)RenderHelper.LOCAL_HEAD_LAYER;
        return mask;
    }

    // From a mirror or camera about to film the wearer, so the body is in the lights for that very
    // frame rather than the next.
    public static void FilmLocalHead()
    {
        double now = Lumora.Core.Engine.Current?.TotalTime ?? 0.0;
        lock (_lock)
        {
            _headFilmedAt = now;
            if (!_headFilmed)
                SetHeadFilmed(true);
        }
    }

    private static void SetHeadFilmed(bool filmed)
    {
        _headFilmed = filmed;
        for (int i = 0; i < _entries.Count; i++)
            Apply(_entries[i]);
    }

    // Any live offscreen view drawing the local head band.
    private static bool AnyFilmOnHead()
    {
        _films.Clear();
        RenderTextureHook.CollectLiveViewports(_films);
        CameraPortalHook.CollectLiveViewports(_films);
        for (int i = 0; i < _films.Count; i++)
        {
            var viewport = _films[i];
            if (viewport.RenderTargetUpdateMode != SubViewport.UpdateMode.Always)
                continue;
            var camera = viewport.GetCamera3D();
            if (camera != null && (camera.CullMask & (uint)RenderHelper.LOCAL_HEAD_LAYER) != 0)
                return true;
        }
        return false;
    }

    private static void EnsureTicking()
    {
        if (_ticking || global::Godot.Engine.GetMainLoop() is not SceneTree tree)
            return;
        _ticking = true;
        tree.ProcessFrame += Tick;
        EngineSettings.Changed += () => _replan = true;
    }

    private static void Tick()
    {
        var engine = Lumora.Core.Engine.Current;
        if (engine == null)
            return;
        double now = engine.TotalTime;
        float dt = double.IsNaN(_lastTick) ? 0f : (float)System.Math.Clamp(now - _lastTick, 0.0, 0.1);
        _lastTick = now;
        bool filming = _entries.Count > 0 && AnyFilmOnHead();

        lock (_lock)
        {
            if (_entries.Count == 0)
                return;

            if (filming)
                _headFilmedAt = now;
            bool filmed = now - _headFilmedAt < HeadHoldSeconds;
            if (filmed != _headFilmed)
                SetHeadFilmed(filmed);

            if (_replan || now - _lastPlan >= PlanInterval)
            {
                _replan = false;
                _lastPlan = now;
                Plan(now);
            }

            float step = dt / FadeSeconds;
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                float target = entry.Granted ? 1f : 0f;
                if (entry.Fade == target)
                    continue;
                entry.Fade = entry.Fade < target
                    ? System.Math.Min(target, entry.Fade + step)
                    : System.Math.Max(target, entry.Fade - step);
                Apply(entry);
            }
        }
    }

    private static void Plan(double now)
    {
        var camera = RenderHelper.ViewerCamera?.Invoke();
        if (camera == null || !GodotObject.IsInstanceValid(camera) || !camera.IsInsideTree())
            camera = (global::Godot.Engine.GetMainLoop() as SceneTree)?.Root?.GetCamera3D();
        bool haveEye = camera != null && GodotObject.IsInstanceValid(camera) && camera.IsInsideTree();
        Vector3 eye = haveEye ? camera!.GlobalPosition : Vector3.Zero;
        bool headset = haveEye && camera is XRCamera3D;

        int budget = headset ? EngineSettings.ShadowedLightsVR : EngineSettings.ShadowedLights;
        float reach = ShadowBudgetPlanner.BaseReach * EngineSettings.ShadowDistanceScale;
        if (haveEye)
            LayOutAtlas(camera!, budget);

        int count = _entries.Count;
        if (_candidates.Length < count)
            _candidates = new ShadowCandidate[System.Math.Max(count, _candidates.Length * 2)];

        for (int i = 0; i < count; i++)
        {
            var entry = _entries[i];
            var node = entry.Node;
            float score = 0f;
            if (entry.Wants && entry.Visible && node != null && GodotObject.IsInstanceValid(node)
                && node.IsInsideTree() && node.IsVisibleInTree())
            {
                // No camera yet: rank by the lights alone, as if the viewer stood at each one.
                float distance = haveEye ? eye.DistanceTo(node.GlobalPosition) : 0f;
                score = ShadowBudgetPlanner.Score(distance, entry.Range, entry.Brightness, reach);
            }
            _candidates[i] = new ShadowCandidate
            {
                Score = score,
                Granted = entry.Granted,
                HeldSeconds = (float)(now - entry.GrantedAt),
            };
        }

        var span = _candidates.AsSpan(0, count);
        bool handedOver = false;
        if (ShadowBudgetPlanner.Plan(span, budget) > 0)
        {
            for (int i = 0; i < count; i++)
            {
                var entry = _entries[i];
                bool granted = span[i].Granted;
                if (granted == entry.Granted)
                    continue;
                entry.Granted = granted;
                handedOver = true;
                if (granted)
                {
                    entry.GrantedAt = now;
                    entry.Band = QuieterBand(entry);
                }
                else if (span[i].Score <= 0f)
                {
                    // Gone from view or out of reach: nothing on screen to fade.
                    entry.Fade = 0f;
                    Apply(entry);
                }
            }
        }

        if (handedOver || budget != _loggedBudget)
        {
            _loggedBudget = budget;
            LogGrants(budget, headset, haveEye, eye);
        }
    }

    private static int QuieterBand(Entry entry)
    {
        int zero = 0, one = 0;
        for (int i = 0; i < _entries.Count; i++)
        {
            var other = _entries[i];
            if (ReferenceEquals(other, entry) || !other.Granted)
                continue;
            if (other.Band == 1) one++;
            else zero++;
        }
        return one < zero ? 1 : 0;
    }

    // ONE SLOT SIZE IN THE WHOLE ATLAS. Godot files each point and spot shadow into an atlas
    // quadrant by how much of the screen the light covers, and moves it to another quadrant as the viewer
    // walks closer or away: a fresh render on every move. When the quadrant it wants is full the move
    // fails, the light keeps its old slot without that slot recording the render, and from its next
    // change on it renders again every frame until a move succeeds. Equal quadrants leave nothing to move
    // to. The budget keeps the count small, so every slot gets the size the default only gave its best
    // few: two slots per point light, room for the budget plus handovers in flight. -xlinka
    private static void LayOutAtlas(Camera3D camera, int budget)
    {
        var viewport = camera.GetViewport();
        if (viewport == null)
            return;
        var subdiv = 2 * (budget + 2) <= 16
            ? Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv4
            : Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv16;
        ulong id = viewport.GetInstanceId();
        if (id == _atlasViewport && (int)subdiv == _atlasSubdiv)
            return;
        _atlasViewport = id;
        _atlasSubdiv = (int)subdiv;
        for (int quadrant = 0; quadrant < 4; quadrant++)
        {
            if (viewport.GetPositionalShadowAtlasQuadrantSubdiv(quadrant) != subdiv)
                viewport.SetPositionalShadowAtlasQuadrantSubdiv(quadrant, subdiv);
        }
    }

    // One line per handover, naming who casts and how far away, so a log can say why a room's shadows
    // look the way they do.
    private static void LogGrants(int budget, bool headset, bool haveEye, Vector3 eye)
    {
        var line = new System.Text.StringBuilder();
        line.Append("Shadow budget: ").Append(ShadowBudgetPlanner.LastGranted).Append(" of ")
            .Append(ShadowBudgetPlanner.LastCandidates).Append(" point/spot lights in reach cast (")
            .Append(budget).Append(" allowed, ").Append(headset ? "headset" : "desktop").Append(')');
        string separator = ": ";
        foreach (var entry in _entries)
        {
            var node = entry.Node;
            if (!entry.Granted || node == null || !GodotObject.IsInstanceValid(node))
                continue;
            var slot = SlotHook.GetSlotFromNode(node);
            line.Append(separator);
            // Lamps are mostly slots called "Light"; the parent says which one.
            if (slot?.Parent != null)
                line.Append(slot.Parent.SlotName.Value).Append('/');
            line.Append(slot?.SlotName.Value ?? node.Name.ToString());
            if (haveEye && node.IsInsideTree())
                line.Append(' ').Append(eye.DistanceTo(node.GlobalPosition).ToString("0.0")).Append(" m");
            line.Append(" r ").Append(entry.Range.ToString("0.#"));
            separator = ", ";
        }
        LumoraLogger.Log(line.ToString());
    }
}
