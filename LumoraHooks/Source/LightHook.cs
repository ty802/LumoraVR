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

    protected override void SyncProperties()
    {
        if (Owner.Type.GetWasChangedAndClear())
            ReplacePlatformNode(BuildLight(Owner.Type.Value));

        var light = PlatformNode;
        if (light == null) return;

        var c = Owner.LightColor.Value;
        light.LightColor = new Color(c.r, c.g, c.b, c.a);
        light.LightEnergy = Owner.Intensity.Value;

        if (light is OmniLight3D omni)
        {
            omni.OmniRange = Owner.Range.Value;
        }
        else if (light is SpotLight3D spot)
        {
            spot.SpotRange = Owner.Range.Value;
            spot.SpotAngle = Owner.SpotAngle.Value;
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
