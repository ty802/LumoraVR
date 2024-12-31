namespace Aquamarine.Source.Scene;

public static class SceneObjectHelpers
{
    public static bool CanInstantiate(this RootObjectType type) =>
        type switch
        {
            RootObjectType.Avatar => true,
            _ => false,
        };
}
