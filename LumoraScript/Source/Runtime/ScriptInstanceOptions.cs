// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

public sealed class ScriptInstanceOptions
{
    public static readonly ScriptInstanceOptions Default = new();

    // What one event call may spend. Fuel still measures the work a call did; by default nothing runs out.
    public long FuelPerCall { get; init; } = long.MaxValue;

    // A call still running after this long is stopped: the whole game has been frozen for that long. It is
    // the one stop on how long a script runs, for a loop that never ends; anything shorter only warns.
    public int MaxCallMilliseconds { get; init; } = 10_000;

    public int MaxCallDepth { get; init; } = 512;

    // All WebAssembly can address. A graph's module asks for what its stack and its texts take, fixed when
    // it starts, and never grows.
    public uint MaxMemoryPages { get; init; } = 65536;

    // Delays waiting to run, and await nodes' work out at once, past which the script is warned.
    public int WarnDelays { get; init; } = 1024;

    public int WarnAwaits { get; init; } = 64;
}
