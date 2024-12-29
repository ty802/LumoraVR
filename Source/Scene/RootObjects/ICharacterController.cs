using Aquamarine.Source.Input;
using Godot;

namespace Aquamarine.Source.Scene.ObjectTypes;

public interface ICharacterController
{
    public Vector3 GetLimbPosition(IInputProvider.InputLimb limb);
    public Quaternion GetLimbRotation(IInputProvider.InputLimb limb);
    public (Vector3 pos, Quaternion rot) GetLimbTransform(IInputProvider.InputLimb limb) => (GetLimbPosition(limb), GetLimbRotation(limb));
}
