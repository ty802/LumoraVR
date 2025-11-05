using Godot;

namespace Aquamarine.Source.Interaction;

/// <summary>
/// Interface for objects that can be grabbed and manipulated.
/// </summary>
public interface IGrabbable
{
    /// <summary>
    /// Called when the object is grabbed.
    /// </summary>
    /// <param name="grabber">The hand that grabbed this object</param>
    void OnGrabbed(Node3D grabber);

    /// <summary>
    /// Called when the object is released.
    /// </summary>
    void OnReleased();

    /// <summary>
    /// Whether this object can currently be grabbed.
    /// </summary>
    bool CanBeGrabbed();

    /// <summary>
    /// The node that should be grabbed (usually the root RigidBody or Node3D).
    /// </summary>
    Node3D GrabbableNode { get; }

    /// <summary>
    /// Whether this object is currently grabbed.
    /// </summary>
    bool IsGrabbed { get; }

    /// <summary>
    /// The highlight color when hovered.
    /// </summary>
    Color HighlightColor { get; }
}
