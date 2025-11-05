using Godot;

namespace Aquamarine.Source.Core.WorldTemplates;

/// <summary>
/// Empty world template - completely blank slate.
/// </summary>
public class EmptyTemplate : WorldTemplate
{
    public override string Name => "Empty";
    public override string Description => "Completely empty world - total creative freedom";
    public override string Category => "Basic";
    public override Color PreviewPrimaryColor => new Color(0.08f, 0.08f, 0.1f);
    public override Color PreviewSecondaryColor => new Color(0.02f, 0.02f, 0.03f);

    public override void Apply(World world)
    {
        world.WorldName.Value = "Empty World";

        // Just spawn point, nothing else
        CreateSpawnPoint(world.RootSlot, Vector3.Zero);
    }
}
