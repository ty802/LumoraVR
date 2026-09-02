// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Godot;
using Lumora.Core;
using Lumora.Core.Components;
using Lumora.Core.Math;

namespace Lumora.Godot.Hooks;

// Light component -> Godot Light3D. Light type (Directional/Point/Spot) maps
// to different Godot subclasses so the platform node is rebuilt via
// ReplacePlatformNode when Owner.Type changes. - xlinka
[ImplementableHook(typeof(Light))]
public class LightHook : NodeBackedComponentHook<Light, Light3D>
{
    public static IHook<Light> Constructor() => new LightHook();

    public Light3D GodotLight => PlatformNode;

    protected override Light3D CreatePlatformNode() => BuildLight(Owner.Type.Value);

    protected override void OnAfterAttach()
    {
        base.OnAfterAttach();
        EngineSettings.Changed += OnSettingsChanged;
    }

    public override void Destroy(bool destroyingWorld)
    {
        EngineSettings.Changed -= OnSettingsChanged;
        base.Destroy(destroyingWorld);
    }

    // Quality settings are global; re-dirty the owner so SyncProperties runs on the main thread instead
    // of poking the Godot node from wherever the setting was written.
    private void OnSettingsChanged()
    {
        var owner = Owner;
        var world = owner?.World;
        if (owner == null || world == null || owner.IsDestroyed)
            return;
        world.RunSynchronously(() =>
        {
            if (!owner.IsDestroyed)
                owner.MarkChangeDirty();
        });
    }

    protected override void SyncProperties()
    {
        if (Owner.Type.GetWasChangedAndClear())
            ReplacePlatformNode(BuildLight(Owner.Type.Value));

        var light = PlatformNode;
        if (light == null) return;

        var c = Owner.LightColor.Value;
        light.LightColor = new Color(c.r, c.g, c.b, c.a);
        light.LightEnergy = Owner.Intensity.Value;

        if (light is DirectionalLight3D directional)
        {
            // The user's global scale rides on top of the light's authored distance: shrinking the
            // cascade range is the cheapest real cut on the shadow pass, and it has to work on worlds
            // whose lights were authored by someone else. -xlinka
            directional.DirectionalShadowMaxDistance =
                System.Math.Max(1f, Owner.ShadowMaxDistance.Value * EngineSettings.ShadowDistanceScale);
            directional.DirectionalShadowMode = Owner.ShadowSplits.Value switch
            {
                ShadowSplitMode.Orthogonal => DirectionalLight3D.ShadowMode.Orthogonal,
                ShadowSplitMode.Two => DirectionalLight3D.ShadowMode.Parallel2Splits,
                _ => DirectionalLight3D.ShadowMode.Parallel4Splits
            };
        }
        else if (light is OmniLight3D omni)
        {
            omni.OmniRange = Owner.Range.Value;
            ApplyDistanceFade(omni);
        }
        else if (light is SpotLight3D spot)
        {
            spot.SpotRange = Owner.Range.Value;
            spot.SpotAngle = Owner.SpotAngle.Value;
            ApplyDistanceFade(spot);
        }

        switch (Owner.Shadows.Value)
        {
            case ShadowType.None:
                light.ShadowEnabled = false;
                break;
            case ShadowType.Hard:
            case ShadowType.Soft:
                light.ShadowEnabled = true;
                break;
        }

        light.ShadowOpacity = 1f - Owner.ShadowStrength.Value;
        light.ShadowBias = Owner.ShadowBias.Value;
        light.ShadowNormalBias = Owner.ShadowNormalBias.Value;
        light.Visible = Owner.Enabled.Value;
    }

    // Zero length is the off switch, not a zero-metre dissolve: Godot reads Begin and Length independently
    // and a length of 0 with fade enabled would pop the light out at Begin with no transition at all. -xlinka
    private void ApplyDistanceFade(Light3D light)
    {
        float length = System.Math.Max(0f, Owner.DistanceFadeLength.Value);
        if (length <= 0f)
        {
            light.DistanceFadeEnabled = false;
            return;
        }

        light.DistanceFadeEnabled = true;
        light.DistanceFadeBegin = System.Math.Max(0f, Owner.DistanceFadeBegin.Value);
        light.DistanceFadeLength = length;
    }

    private static Light3D BuildLight(LightType type)
    {
        return type switch
        {
            LightType.Directional => new DirectionalLight3D { Name = "DirectionalLight" },
            LightType.Point => new OmniLight3D { Name = "PointLight" },
            LightType.Spot => new SpotLight3D { Name = "SpotLight" },
            _ => throw new ArgumentException($"Unknown light type: {type}")
        };
    }
}
