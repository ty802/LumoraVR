// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// How many times an Every N Seconds has fired since the script started, this time included.
public readonly record struct IntervalElapsed(int Count);
