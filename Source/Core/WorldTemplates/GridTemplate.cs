using Godot;
using Aquamarine.Source.Core.Components;

namespace Aquamarine.Source.Core.WorldTemplates;

/// <summary>
/// Grid world template - infinite grid floor with spawn point.
/// The classic VR starting template.
/// </summary>
public class GridTemplate : WorldTemplate
{
    public override string Name => "Grid";
    public override string Description => "Infinite grid floor perfect for building and testing";
    public override string Category => "Basic";
    public override Color PreviewPrimaryColor => new Color(0.12f, 0.36f, 0.42f);
    public override Color PreviewSecondaryColor => new Color(0.01f, 0.08f, 0.11f);

    public override void Apply(World world)
    {
        world.WorldName.Value = "Grid World";

        // Create grid floor
        CreateGridFloor(world.RootSlot, size: 50, spacing: 1.0f);

        // Create sun
        CreateSunLight(world.RootSlot);

        // Create spawn point
        CreateSpawnPoint(world.RootSlot, new Vector3(0, 1, 0));

        // Add ambient light
        var ambientSlot = world.RootSlot.AddSlot("Ambient Light");
        var ambient = ambientSlot.AttachComponent<LightComponent>();
        ambient.Type.Value = LightComponent.LightType.Directional;
        ambient.LightColor.Value = new Color(0.3f, 0.3f, 0.4f);
        ambient.Energy.Value = 0.3f;
        ambient.CastShadow.Value = false;
    }
}
