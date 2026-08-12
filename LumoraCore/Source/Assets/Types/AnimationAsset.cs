// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Threading.Tasks;
using Lumora.Core.Assets.Animation;

namespace Lumora.Core.Assets;

// A decoded animation clip, gathered from its URL and shared by every requester for that URL.
//
// There is no platform-side counterpart and no asset hook. A clip is numbers that the engine samples
// itself: nothing about it needs a renderer object, so it decodes fully here and never has to wait on
// a frame boundary the way a mesh or texture upload does. That also means the whole asset works
// headless. -xlinka
public sealed class AnimationAsset : LoadableAsset
{
    public AnimationClip? Clip { get; private set; }

    // Seconds. 0 when nothing is loaded.
    public float Duration => Clip?.Duration ?? 0f;

    public int TrackCount => Clip?.TrackCount ?? 0;

    protected override async Task LoadSelf()
    {
        var bytes = await AssetManager.RequestGather(AssetURL).ConfigureAwait(false);
        if (bytes == null || bytes.Length == 0)
        {
            FailLoad($"No animation data gathered for {AssetURL}");
            return;
        }

        if (!AnimationClip.LooksLikeClip(bytes))
        {
            // Fail loudly rather than throwing a parse exception from inside the decoder: this is
            // almost always a URL pointing at the wrong asset, and the magic check names it exactly.
            FailLoad($"Data at {AssetURL} is not an animation clip");
            return;
        }

        try
        {
            Clip = AnimationClip.FromBytes(bytes);
        }
        catch (System.Exception ex)
        {
            FailLoad($"Failed to decode animation clip from {AssetURL}: {ex.Message}");
            return;
        }

        Version++;
    }

    public override void Unload()
    {
        Clip = null;
    }
}
