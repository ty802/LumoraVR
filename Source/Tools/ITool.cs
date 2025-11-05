using Godot;

namespace Aquamarine.Source.Tools;

/// <summary>
/// Base interface for all developer tools.
/// Tools can be equipped, used, and interact with the world.
/// </summary>
public interface ITool
{
    /// <summary>
    /// Called when the tool is equipped to a hand.
    /// </summary>
    void OnEquipped(ToolSlot slot);

    /// <summary>
    /// Called when the tool is unequipped from a hand.
    /// </summary>
    void OnUnequipped();

    /// <summary>
    /// Called every frame while the tool is equipped.
    /// </summary>
    void OnUpdate(double delta);

    /// <summary>
    /// Called when the primary action button is pressed.
    /// </summary>
    void OnPrimaryAction();

    /// <summary>
    /// Called when the primary action button is released.
    /// </summary>
    void OnPrimaryActionRelease();

    /// <summary>
    /// Called when the secondary action button is pressed.
    /// </summary>
    void OnSecondaryAction();

    /// <summary>
    /// Called when the secondary action button is released.
    /// </summary>
    void OnSecondaryActionRelease();

    /// <summary>
    /// The name of this tool.
    /// </summary>
    string ToolName { get; }

    /// <summary>
    /// The 3D mesh/scene for this tool.
    /// </summary>
    Node3D ToolMesh { get; }

    /// <summary>
    /// Whether this tool is currently equipped.
    /// </summary>
    bool IsEquipped { get; }
}
