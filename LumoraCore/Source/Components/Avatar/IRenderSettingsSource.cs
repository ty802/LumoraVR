// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Avatar;

// What the eye cameras read for their clip planes. Null means "no opinion", and the head output falls
// back to the engine defaults (5 mm scaled by the user, 4096 m). Extends IWorldElement so the user
// root can hold the current source in a replicated SyncRef.
public interface IRenderSettingsSource : IWorldElement
{
    float? NearClip { get; }
    float? FarClip { get; }
}
