// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;

namespace Lumora.Core.Components.Audio;

// A component an AudioOutput is allowed to point at. The mixer contract is IAudioDataSource; this
// marker is what keeps a SyncRef<Component> from being pointed at a mesh renderer and silently doing
// nothing, and what the browser can filter on later. -xlinka
public interface IWorldAudioDataSource : IAudioDataSource
{
}
