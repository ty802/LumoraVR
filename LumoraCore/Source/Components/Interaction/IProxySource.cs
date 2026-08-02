// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// A UI element that offers a grabbable stand-in when gripped: pulling on an inspector row hands
// the grabber a small reference card instead of moving the panel. Implementations decide what the
// card points at (a slot, a component, a sync member).
public interface IProxySource
{
    // null when there is nothing to pull
    IGrabbable? TryCreateProxy(Grabber grabber, in float3 spawnPoint);
}
