namespace Aquamarine.Source.Scene;

public enum RootObjectType
{
    Avatar,
    PlayerCharacterController,
}

public enum ChildObjectType
{
    Node,
    Mesh,
    Armature,
}

public static class SceneObjectHelpers
{
    public static bool CanInstantiate(this RootObjectType type) =>
        type switch
        {
            RootObjectType.Avatar => true,
            _ => false,
        };
}
