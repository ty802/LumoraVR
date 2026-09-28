// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// An event that happens ON something in the world, a press on a button for one. The graph may name that
// something with a "source" binding; without one, the host listens where the script itself sits. The
// compiler only carries the binding along; what listening means is the host's business.
//
// SourceType is the script type the source is bound as: a slot unless said otherwise, and an event whose
// source is not a slot has nothing to listen to without one.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SourcedEventAttribute : Attribute
{
    public string SourceType { get; }

    public SourcedEventAttribute(string sourceType = "slot")
    {
        SourceType = sourceType;
    }
}
