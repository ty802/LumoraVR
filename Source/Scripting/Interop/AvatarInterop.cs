using Aquamarine.Source.Scene.ObjectTypes;

namespace Aquamarine.Source.Scripting.Interop;

public static class AvatarInterop
{
    public static string AvatarGetLeftHandBone(Avatar avatar) => avatar.LeftHandBone;
    public static string AvatarGetRightHandBone(Avatar avatar) => avatar.RightHandBone;
}
