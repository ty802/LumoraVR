// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;

namespace Lumora.Core.Components.Audio;

// Takes its slot away once the clip on it has played through: a sound a script played once at a point.
// The authority removes a shared one, which is world content; a local one goes on the machine it is on.
// A clip that will not load goes after a few seconds, and nothing stays longer than a minute. -xlinka
[ComponentCategory("Hidden")]
public sealed class OneShotSound : Component
{
    public const double MaxSeconds = 60.0;
    private const double GraceSeconds = 0.5;
    private const double LoadWaitSeconds = 3.0;

    private double _age;
    private double _finishedFor;

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        var world = World;
        if (world == null || IsDestroyed || (!world.IsAuthority && !IsLocalElement))
            return;
        _age += delta;
        if (Done(delta))
            Remove();
    }

    private bool Done(float delta)
    {
        if (_age >= MaxSeconds)
            return true;
        var player = Slot?.GetComponent<AudioClipPlayer>();
        if (player == null || player.IsDestroyed || player.Clip.Target == null)
            return true;
        if (player.IsFinished)
        {
            _finishedFor += delta;
            return _finishedFor >= GraceSeconds;
        }
        if (_age < LoadWaitSeconds || player.Clip.Target is not IUrlAssetProvider { IsLoadPending: false } || player.Clip.Asset != null)
            return false;
        if (player.LoadError is { } why)
            Logging.Logger.Warn($"One Shot: nothing played, the clip did not load: {why}");
        return true;
    }

    private void Remove()
    {
        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
            return;
        using (World?.DataModelPermissions?.EnterAuthoredContentScope(this))
            slot.Destroy();
    }
}
