using Godot;

namespace Aquamarine.Source.Input;

public interface IInputProvider
{
    public static IInputProvider Instance;
    
    public bool IsVR { get; }
    
    public enum InputLimb
    {
        Head,
        LeftHand,
        RightHand,
        Hip,
        LeftFoot,
        RightFoot,
    }
    public Vector3 GetPlayspaceMovementDelta { get; }
    public Vector2 GetMovementInputAxis { get; }
    public Vector3 GetLimbPosition(InputLimb limb);
    public Quaternion GetLimbRotation(InputLimb limb);
    public void MoveTransform(Transform3D transform);

    public static Vector3 PlayespaceMovementDelta => Instance?.GetPlayspaceMovementDelta ?? Vector3.Zero;
    public static Vector3 LimbPosition(InputLimb limb) => Instance?.GetLimbPosition(limb) ?? Vector3.Zero;
    public static Quaternion LimbRotation(InputLimb limb) => Instance?.GetLimbRotation(limb) ?? Quaternion.Identity;

    public static (Vector3 pos, Quaternion rot) LimbTransform(InputLimb limb) => (LimbPosition(limb), LimbRotation(limb));
    public static void Move(Transform3D transform) => Instance?.MoveTransform(transform);
    public static Vector2 MovementInputAxis => Instance?.GetMovementInputAxis ?? Vector2.Zero;
}

