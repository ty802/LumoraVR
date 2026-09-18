// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar.IK;

internal sealed class ProceduralGrounding
{
    private readonly List<Slot> _exclude = new(1);
    private float3 _previousRoot;
    private bool _initialized;
    private float _stationaryTime;

    public bool Grounded { get; private set; } = true;
    public bool WasGrounded { get; private set; } = true;
    public float3 RootDelta { get; private set; }

    public void Update(UserRoot? user, float dt)
    {
        var root = user?.Slot;
        if (root == null || root.IsDestroyed || dt <= 0f)
            return;

        float3 position = root.GlobalPosition;
        RootDelta = _initialized ? position - _previousRoot : float3.Zero;
        _previousRoot = position;
        _initialized = true;
        WasGrounded = Grounded;

        var controller = root.GetComponent<CharacterController>();
        if (user!.IsLocalUserRoot && controller is { IsReady: true, Hook: ICharacterControllerHook })
        {
            Grounded = controller.IsGrounded();
            return;
        }

        float scale = MathF.Max(user.GlobalScale, 0.01f);
        float verticalSpeed = RootDelta.y / dt;
        _exclude.Clear();
        _exclude.Add(root);
        var hit = root.World.Physics.Raycast(position + float3.Up * (0.1f * scale), float3.Down, _exclude, 0.2f * scale);
        if (hit.HasValue)
        {
            Grounded = MathF.Abs(position.y - hit.Value.Point.y) <= 0.06f * scale
                       && verticalSpeed <= 0.5f * scale;
            _stationaryTime = 0f;
        }
        else
        {
            // Replicated roots and previews may have no local collision surface.
            _stationaryTime = MathF.Abs(verticalSpeed) < 0.1f * scale ? _stationaryTime + dt : 0f;
            if (MathF.Abs(verticalSpeed) >= 0.1f * scale)
                Grounded = false;
            else if (_stationaryTime >= 0.08f)
                Grounded = true;
        }
    }

    public void Reset()
    {
        _initialized = false;
        _stationaryTime = 0f;
        RootDelta = float3.Zero;
        Grounded = WasGrounded = true;
    }
}
