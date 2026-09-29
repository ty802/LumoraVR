// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// Where a watched player was, in seconds, when it stopped running.
public readonly record struct MediaStopped(float Position);
