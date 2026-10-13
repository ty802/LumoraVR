// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Audio;

// Which sample of a received voice is reaching the ear right now, on that stream's own ring index.
//
// While the mixer is reading the stream it is the read cursor less whatever sits in the device after
// it. When the cursor stops (the talker paused and the ring ran dry), what was last read is still
// playing out of the device, so the position carries on from there in real time until it meets the
// cursor. It never runs backwards; a cursor more than a second behind it is a new ring and starts it
// over. World thread only. -xlinka
public sealed class VisemeClock
{
    // Longer than the mixer ever goes between reads of a stream that is playing.
    private const double IdleSeconds = 0.05;

    // Anything past this is a broken number, not a device.
    private const double MaxLatencySeconds = 2.0;

    private bool _started;
    private long _heard;
    private long _cursor;
    private long _anchor;
    private double _anchorTime;
    private double _time;
    private double _idle;

    public long Heard => _heard;

    // Nothing new has reached the ear for a while: the voice stopped, or nothing is playing it.
    public bool IsIdle => _started && _idle >= IdleSeconds;

    // cursor: the stream's read index. latencySeconds: from the mixer reading a sample to it being heard.
    public long Update(long cursor, double latencySeconds, int sampleRate, double deltaSeconds)
    {
        if (!(deltaSeconds > 0.0))
            deltaSeconds = 0.0;
        if (!(latencySeconds > 0.0))
            latencySeconds = 0.0;
        else if (latencySeconds > MaxLatencySeconds)
            latencySeconds = MaxLatencySeconds;
        if (sampleRate <= 0)
            sampleRate = AudioSimulator.DefaultSampleRate;

        _time += deltaSeconds;

        long estimate;
        if (!_started || cursor != _cursor)
        {
            estimate = cursor - (long)(latencySeconds * sampleRate);
            _cursor = cursor;
            _anchor = estimate;
            _anchorTime = _time;
        }
        else
        {
            estimate = System.Math.Min(cursor, _anchor + (long)((_time - _anchorTime) * sampleRate));
        }

        if (!_started || estimate < _heard - sampleRate)
        {
            _started = true;
            _heard = estimate;
            _idle = 0.0;
            return _heard;
        }

        if (estimate > _heard)
        {
            _heard = estimate;
            _idle = 0.0;
        }
        else
        {
            _idle += deltaSeconds;
        }
        return _heard;
    }

    public void Reset()
    {
        _started = false;
        _heard = 0L;
        _cursor = 0L;
        _anchor = 0L;
        _anchorTime = 0.0;
        _time = 0.0;
        _idle = 0.0;
    }
}
