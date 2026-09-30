// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading;

namespace Lumora.Audio;

// Settings on their way from the world thread to the audio thread: an immutable box swapped by reference, so
// the audio thread never waits and never sees half a change. A value equal to the one already staged is
// dropped, which makes pushing the same settings every frame free. One writer at a time.
internal sealed class StagedSettings<T> where T : struct, IEquatable<T>
{
    private sealed class Box
    {
        public readonly T Value;

        public Box(in T value) => Value = value;
    }

    private Box _staged;
    private Box _taken;

    public StagedSettings(in T initial)
    {
        _staged = new Box(initial);
        _taken = _staged;
    }

    // Any thread: the value most recently written.
    public T Value => Volatile.Read(ref _staged).Value;

    // Audio thread: what the last Take picked up.
    public ref readonly T Current => ref _taken.Value;

    public void Write(in T value)
    {
        if (Volatile.Read(ref _staged).Value.Equals(value))
            return;
        Volatile.Write(ref _staged, new Box(value));
    }

    // Audio thread: true when something written since the last call has become Current.
    public bool Take()
    {
        var box = Volatile.Read(ref _staged);
        if (ReferenceEquals(box, _taken))
            return false;
        _taken = box;
        return true;
    }
}
