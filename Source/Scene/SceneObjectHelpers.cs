using System;
using Aquamarine.Source.Scene.RootObjects;

namespace Aquamarine.Source.Scene;

public static class SceneObjectHelpers
{
    public static bool CanInstantiate(this RootObjectType type) =>
        type switch
        {
            RootObjectType.Avatar => true,
            _ => false,
        };
    public static Type GetCorrespondingType(this RootObjectType type) => 
        type switch
        {
            RootObjectType.Avatar => typeof(Avatar),
            RootObjectType.PlayerCharacterController => typeof(PlayerCharacterController),
            _ => null,
        };
    public static bool MatchesObject(this RootObjectType type, IRootObject obj) => obj.GetType() == type.GetCorrespondingType();
}
