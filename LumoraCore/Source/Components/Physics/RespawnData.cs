// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

[ComponentCategory("Physics")]
public class RespawnData : Component
{
    public readonly Sync<float3> OriginalPosition = null!;
    public readonly Sync<floatQ> OriginalRotation = null!;

    private RespawnRegistry? _registry;

    public override void OnStart()
    {
        base.OnStart();
        _registry = RespawnRegistry.For(World);
        _registry?.Register(this);
    }

    public override void OnDestroy()
    {
        _registry?.Unregister(this);
        _registry = null;
        base.OnDestroy();
    }

    public void StoreCurrentPosition()
    {
        OriginalPosition.Value = Slot.GlobalPosition;
        OriginalRotation.Value = Slot.GlobalRotation;
    }
}
