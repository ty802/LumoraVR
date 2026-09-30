// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Audio;

// A filter that keeps sounding after its input stops (a reverb, a delay). HasTail is read on the audio
// thread, straight after Process, by whatever wraps the filter and wants to know whether taking it out
// of the chain would cut something off.
public interface IAudioTailFilter
{
    bool HasTail { get; }
}
