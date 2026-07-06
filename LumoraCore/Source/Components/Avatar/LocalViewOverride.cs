// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Overrides a slot's rendered transform when viewed from a specific rendering context.
///
/// Primary use: scale the local user's head to zero in UserView so the player
/// doesn't see their own avatar head, while other users and floor shadows are unaffected.
///
/// Usage:
///   var lvo = headVisual.AttachComponent<LocalViewOverride>();
///   lvo.Context.Value          = ViewContext.UserView;
///   lvo.HasScaleOverride.Value = true;
///   lvo.ScaleOverride.Value    = float3.Zero;
/// </summary>
[ComponentCategory("Users/Avatar")]
public class LocalViewOverride : ImplementableComponent<IHook>
{
    /// <summary>Which rendering context activates this override.</summary>
    public readonly Sync<ViewContext> Context = new();

    /// <summary>When true, PositionOverride replaces the slot's normal position.</summary>
    public readonly Sync<bool>   HasPositionOverride = new();
    public readonly Sync<float3> PositionOverride    = new();

    /// <summary>When true, RotationOverride replaces the slot's normal rotation.</summary>
    public readonly Sync<bool>   HasRotationOverride = new();
    public readonly Sync<floatQ> RotationOverride    = new();

    /// <summary>
    /// When true, ScaleOverride replaces the slot's normal scale.
    /// Set to float3.Zero to make the slot invisible in the target context
    /// while keeping shadow casting intact.
    /// </summary>
    public readonly Sync<bool>   HasScaleOverride = new();
    public readonly Sync<float3> ScaleOverride    = new();

    private readonly UserRootRegistrationTracker _userRootReg;

    public LocalViewOverride()
    {
        _userRootReg = new UserRootRegistrationTracker(this);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        _userRootReg.Attach();
    }

    public override void OnInit()
    {
        base.OnInit();
        Context.Value          = ViewContext.UserView;
        // HasPositionOverride = false (C# default, skip)
        // PositionOverride = float3.Zero (C# default, skip)
        // HasRotationOverride = false (C# default, skip)
        RotationOverride.Value = floatQ.Identity;
        // HasScaleOverride = false (C# default, skip)
        ScaleOverride.Value    = float3.One;
    }

    // The hook also gates on the external-camera state (third-person/free-cam
    // must show the full avatar); poke it when that flips since no sync field
    // changes.
    private bool _lastExternalCamera;

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        bool external = UserInputState.FocusedExternalCameraActive;
        if (external != _lastExternalCamera)
        {
            _lastExternalCamera = external;
            RunApplyChanges();
        }
    }

    public override void OnDestroy()
    {
        _userRootReg.Detach();
        base.OnDestroy();
    }
}
