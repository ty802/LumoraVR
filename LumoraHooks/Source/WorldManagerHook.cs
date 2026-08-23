// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core.Management;

namespace Lumora.Godot.Hooks;

public class WorldManagerHook : IWorldManagerHook
{
    public WorldManager Owner { get; private set; } = null!;

    public Node3D Root { get; private set; } = null!;

    public static WorldManagerHook Instance { get; private set; } = null!;

    public static WorldManagerHook Constructor()
    {
        return new WorldManagerHook();
    }

    public void Initialize(WorldManager owner, object sceneRoot)
    {
        Owner = owner;
        Instance = this;

        Root = new Node3D();
        Root.Name = "WorldManager";

        if (sceneRoot is Node node)
        {
            node.AddChild(Root);
        }

        Root.Position = Vector3.Zero;
        Root.Rotation = Vector3.Zero;
        Root.Scale = Vector3.One;

        Owner.WorldAdded += OnWorldAdded;
        Owner.WorldRemoved += OnWorldRemoved;

        foreach (var world in Owner.Worlds)
        {
            OnWorldAdded(world);
        }
    }

    private void OnWorldAdded(Lumora.Core.World world)
    {
        // must happen before any slots are created
        var worldHook = WorldHook.Constructor();
        world.Hook = worldHook;  // Set hook FIRST
        worldHook.Initialize(world);   // Then initialize it

    }

    private void OnWorldRemoved(Lumora.Core.World world)
    {
        if (world.Hook is WorldHook worldHook)
        {
            worldHook.Destroy();
            world.Hook = null!;
        }

    }

    public void Destroy()
    {
        if (Root != null && GodotObject.IsInstanceValid(Root))
        {
            Root.QueueFree();
        }

        if (Instance == this)
        {
            Instance = null!;
        }

        Root = null!;
        Owner = null!;
    }
}

