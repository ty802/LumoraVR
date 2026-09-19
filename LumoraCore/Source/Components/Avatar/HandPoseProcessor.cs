// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Input;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Avatar;

// A source that derives its pose from other sources. Subclasses fill the per-side buffers in
// UpdatePoses; the first read in a frame runs it, later reads in the same frame hit the buffers. If
// UpdatePoses throws the component disables itself after one warning rather than throwing every
// frame from a broken graph. Local-only: what a processor produces never goes on the wire, every
// peer derives it from the same replicated inputs. -xlinka
public abstract class HandPoseProcessor : Component, IHandPoseSourceComponent
{
    protected sealed class SideData
    {
        public readonly float3[] Positions = HandPoseHelper.NewPositions();
        public readonly floatQ[] Rotations = HandPoseHelper.NewRotations();
        public bool HasPositions;
        public bool Tracking;
    }

    private readonly SideData _left = new();
    private readonly SideData _right = new();
    private ulong _lastUpdate = ulong.MaxValue;
    private bool _metacarpals;
    private bool _faulted;

    public bool TracksMetacarpals
    {
        get
        {
            EnsureUpdated();
            return _metacarpals;
        }
    }

    public bool IsHandTracked(Chirality side)
    {
        if (!Enabled.Value)
            return false;
        EnsureUpdated();
        return Data(side)?.Tracking ?? false;
    }

    public bool TryGetFingerPosition(BodyNode node, out float3 handLocalPosition)
    {
        handLocalPosition = float3.Zero;
        int index = node.GetFingerSegmentIndex();
        var data = index >= 0 ? Data(node.GetChirality()) : null;
        if (data == null || !IsHandTracked(node.GetChirality()) || !data.HasPositions)
            return false;
        handLocalPosition = data.Positions[index];
        return true;
    }

    public bool TryGetFingerRotation(BodyNode node, out floatQ handLocalRotation)
    {
        handLocalRotation = floatQ.Identity;
        int index = node.GetFingerSegmentIndex();
        var data = index >= 0 ? Data(node.GetChirality()) : null;
        if (data == null || !IsHandTracked(node.GetChirality()))
            return false;
        handLocalRotation = data.Rotations[index];
        return true;
    }

    protected SideData Data(Chirality side)
        => side == Chirality.Left ? _left : side == Chirality.Right ? _right : null!;

    protected void SetTracksMetacarpals(bool value) => _metacarpals = value;

    protected abstract void UpdatePoses();

    private void EnsureUpdated()
    {
        if (_faulted || World == null)
            return;
        ulong frame = World.Time.UpdateIndex;
        if (frame == _lastUpdate)
            return;
        _lastUpdate = frame;

        try
        {
            UpdatePoses();
        }
        catch (Exception ex)
        {
            _faulted = true;
            _left.Tracking = false;
            _right.Tracking = false;
            _metacarpals = false;
            LumoraLogger.Warn($"{GetType().Name}: pose update threw, disabling. {ex.Message}");
            Enabled.Value = false;
        }
    }
}
