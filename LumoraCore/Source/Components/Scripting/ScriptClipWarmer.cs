// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;

namespace Lumora.Core.Components.Scripting;

// Keeps the clips a script is bound to loaded on every machine while the script may play them, so a
// sound played once starts at its start rather than after a download. Put there by the authority and
// never saved. -xlinka
[ComponentCategory("Hidden")]
public sealed class ScriptClipWarmer : Component
{
    public readonly SyncRef<ScriptHost> Host = new();
    public readonly SyncAssetList<AudioClip> Clips = new();
}
