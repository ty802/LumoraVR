// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Media;

// What a decoder can say about itself at a glance, for the player's stats readout. Plain numbers,
// read once a poll from the world thread, so nothing here reaches into a decoder thread. -xlinka
public struct VideoStats
{
    public long VideoFrames;
    public long AudioBlocks;
    public long AudioBlocksPushed;
    public long AudioBlocksLate;
    public int AudioQueued;
    public double AudioLeadSeconds;
    // Where the decoder believes it is, or a negative number before it has said anything.
    public double DecoderSeconds;
    public double WantedSeconds;
    public string? State;
}
