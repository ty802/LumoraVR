// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Diagnostics;

namespace Lumora.Core;

// The clock every peer in a session agrees on, in seconds.
//
// Time-phased components (a spinner, an oscillator, an animator's anchor) compute their state from an
// instant rather than accumulating it per frame, so they need an instant that means the same thing on
// every machine. World.Time.TotalTime cannot be it: it starts at zero when each peer OPENS the world,
// so the same number is a different real moment per peer and a late joiner starts from scratch. Raw
// UTC was the stand-in, and it is only ever as good as the two machines' clocks happen to agree - and
// it can be stepped underneath us by an NTP correction or somebody setting the date.
//
// So: the AUTHORITY's reading is the session's reading, and every other peer estimates its own offset
// from it by probing. The timeline stays UTC-shaped on purpose. An uncorrected peer then reads what it
// always read, the correction is the machines' actual skew rather than a jump between two unrelated
// number ranges, and an animator anchor written before any of this still decodes to the right frame.
// The authority's offset is zero by construction, so a solo world and a host both work with no network
// at all and pay nothing for this. -xlinka
public sealed class SessionClock
{
    // Local reading. Anchored to UTC once, then advanced by a monotonic timer, so it keeps wall-clock
    // magnitude without inheriting wall-clock jumps.
    private static readonly double TimestampToSeconds = 1.0 / Stopwatch.Frequency;
    private readonly double _utcAnchorSeconds = DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();

    // Correction, slewed rather than stepped: a phase that jumps makes a spinner snap and an animation
    // skip frames, and the whole point of a shared clock is that nobody can see it working. Held below
    // 1.0 so the corrected clock still advances monotonically even while it is catching up. -xlinka
    private const double MaxSlewRate = 0.2;

    private const double MaxAcceptedRoundTrip = 2.0;

    // Spacing of the opening burst, and how many of them, so a joiner converges in well under a second.
    private const double AcquireInterval = 0.25;
    private const int AcquireProbes = 4;

    // Spacing once acquired. Clock drift between machines is a matter of parts per million.
    private const double SteadyInterval = 5.0;

    private readonly object _sampleLock = new();
    private double _targetOffset;
    private bool _hasSample;
    private bool _offsetAcquired;
    private double _bestRoundTrip = double.MaxValue;
    private double _lastRoundTrip;
    private double _nextProbeAt;
    private int _probesSent;

    // Update-thread only: written by Advance, read by everything that asks the time.
    private double _appliedOffset;

    // Before any correction.
    public double LocalSeconds => _utcAnchorSeconds + (Stopwatch.GetTimestamp() - _startTimestamp) * TimestampToSeconds;

    // Monotonic and continuous once acquired; the single exception is the first authority sample, which lands
    // whole because there is no earlier value on this timeline worth being continuous with.
    public double SessionSeconds => LocalSeconds + _appliedOffset;

    // False on the authority itself and on a solo world, where the local reading IS the session reading.
    public bool IsSynchronized
    {
        get { lock (_sampleLock) { return _offsetAcquired; } }
    }

    // Diagnostics.
    public double Offset => _appliedOffset;

    // Seconds. Diagnostics.
    public double LastRoundTrip
    {
        get { lock (_sampleLock) { return _lastRoundTrip; } }
    }

    internal void Advance(double delta)
    {
        double target;
        bool landWhole;
        lock (_sampleLock)
        {
            if (!_hasSample)
                return;
            target = _targetOffset;
            landWhole = !_offsetAcquired;
            _offsetAcquired = true;
        }

        if (landWhole)
        {
            _appliedOffset = target;
            return;
        }

        double error = target - _appliedOffset;
        double step = MaxSlewRate * (delta > 0 ? delta : 0);
        if (error > step)
            error = step;
        else if (error < -step)
            error = -step;
        _appliedOffset += error;
    }

    // Called from the network loop on a peer that has an authority to ask.
    public bool TryTakeProbe(out double stamp)
    {
        double now = LocalSeconds;
        lock (_sampleLock)
        {
            if (now < _nextProbeAt)
            {
                stamp = 0;
                return false;
            }
            _probesSent++;
            _nextProbeAt = now + (_probesSent < AcquireProbes ? AcquireInterval : SteadyInterval);
        }
        stamp = now;
        return true;
    }

    public void ReceiveReply(double sentStamp, double authoritySeconds)
    {
        double now = LocalSeconds;
        double roundTrip = now - sentStamp;

        // A negative round trip means the stamp was not ours; a huge one means the probe sat behind
        // something on a reliable channel and its midpoint is a guess, not a measurement.
        if (!double.IsFinite(roundTrip) || !double.IsFinite(authoritySeconds)
            || roundTrip < 0 || roundTrip > MaxAcceptedRoundTrip)
            return;

        // Half the round trip is the one-way estimate, which is only honest when the two legs are
        // roughly equal. Keep the quick probes and drop the ones that queued, instead of averaging a
        // stall into the answer. The floor creeps up so one lucky sample cannot lock everything else
        // out for the rest of the session. -xlinka
        lock (_sampleLock)
        {
            if (_hasSample && roundTrip > _bestRoundTrip * 1.5 + 0.005)
            {
                _bestRoundTrip *= 1.05;
                return;
            }
            if (roundTrip < _bestRoundTrip)
                _bestRoundTrip = roundTrip;
            _lastRoundTrip = roundTrip;

            double offset = authoritySeconds + roundTrip * 0.5 - now;
            _targetOffset = _hasSample ? _targetOffset + (offset - _targetOffset) * 0.25 : offset;
            _hasSample = true;
        }
    }

    // WIRE FORMAT
    // Two doubles at most, on the existing control channel. The probe carries the sender's stamp and
    // gets it back untouched, so the authority holds no per-peer clock state and a reply that arrives
    // after the peer gave up on it is simply rejected by its own round trip.

    public static byte[] EncodeProbe(double stamp)
    {
        var payload = new byte[8];
        BitConverter.TryWriteBytes(payload.AsSpan(0, 8), stamp);
        return payload;
    }

    public static bool TryDecodeProbe(byte[]? payload, out double stamp)
    {
        if (payload is not { Length: 8 })
        {
            stamp = 0;
            return false;
        }
        stamp = BitConverter.ToDouble(payload, 0);
        return true;
    }

    public static byte[] EncodeReply(double stamp, double authoritySeconds)
    {
        var payload = new byte[16];
        BitConverter.TryWriteBytes(payload.AsSpan(0, 8), stamp);
        BitConverter.TryWriteBytes(payload.AsSpan(8, 8), authoritySeconds);
        return payload;
    }

    public static bool TryDecodeReply(byte[]? payload, out double stamp, out double authoritySeconds)
    {
        if (payload is not { Length: 16 })
        {
            stamp = 0;
            authoritySeconds = 0;
            return false;
        }
        stamp = BitConverter.ToDouble(payload, 0);
        authoritySeconds = BitConverter.ToDouble(payload, 8);
        return true;
    }
}
