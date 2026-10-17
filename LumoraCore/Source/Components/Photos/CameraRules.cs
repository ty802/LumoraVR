// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core.Components;

// A world's say over cameras, on the world's root: which ways a camera may move (the listed modes are the
// only ones allowed, or the ones refused), and whether a camera may frame anyone but the person running it.
// The host puts a camera back inside the rules the moment it steps out: framing its own operator again, in
// the first mode allowed, or gone when no mode is. -xlinka
[ComponentCategory("World")]
public class CameraRules : Component
{
    public readonly Sync<bool> OnlyListedModes = new();
    public readonly SyncFieldList<PhotoCameraMode> Modes = new();
    public readonly Sync<bool> FramingOthers = new(true);

    private static readonly PhotoCameraMode[] AllModes =
    {
        PhotoCameraMode.FirstPerson, PhotoCameraMode.ThirdPerson, PhotoCameraMode.Group, PhotoCameraMode.Stationary, PhotoCameraMode.Manual,
    };

    public static CameraRules? For(World? world)
    {
        var root = world?.RootSlot;
        return root == null || root.IsDestroyed ? null : root.GetComponent<CameraRules>();
    }

    public static bool ModeAllowed(World? world, PhotoCameraMode mode) => For(world)?.Allows(mode) ?? true;

    public static bool FramingOthersAllowed(World? world) => For(world)?.FramingOthers.Value ?? true;

    public bool Allows(PhotoCameraMode mode)
    {
        bool listed = false;
        foreach (var entry in Modes)
        {
            if (entry == mode)
            {
                listed = true;
                break;
            }
        }
        return OnlyListedModes.Value ? listed : !listed;
    }

    public PhotoCameraMode? FirstAllowed()
    {
        foreach (var mode in AllModes)
        {
            if (Allows(mode))
                return mode;
        }
        return null;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        var world = World;
        if (world == null || !world.IsAuthority)
            return;
        List<PhotoCamera>? gone = null;
        foreach (var camera in PhotoCamera.InWorld(world))
        {
            if (camera.IsDestroyed)
                continue;
            if (!FramingOthers.Value && camera.Subject.Target != null && !ReferenceEquals(camera.Subject.Target, camera.Operator.Target))
                camera.Subject.Target = null!;
            if (Allows(camera.Mode.Value))
                continue;
            if (FirstAllowed() is { } mode)
                camera.Mode.Value = mode;
            else
                (gone ??= new List<PhotoCamera>()).Add(camera);
        }
        if (gone != null)
        {
            foreach (var camera in gone)
                camera.Slot.Destroy();
        }
    }
}
