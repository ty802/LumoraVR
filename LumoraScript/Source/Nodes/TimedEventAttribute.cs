// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// An event the host raises on a clock, every "seconds" set on the node. -xlinka
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TimedEventAttribute : Attribute
{
}
