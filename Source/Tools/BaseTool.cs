using Godot;

namespace Aquamarine.Source.Tools;

/// <summary>
/// Base class for all developer tools.
/// Provides common functionality and default implementations.
/// </summary>
public abstract partial class BaseTool : Node3D, ITool
{
    protected ToolSlot _equippedSlot;
    protected bool _isEquipped;
    protected Node3D _toolMesh;

    public abstract string ToolName { get; }
    public Node3D ToolMesh => _toolMesh;
    public bool IsEquipped => _isEquipped;

    public virtual void OnEquipped(ToolSlot slot)
    {
        _equippedSlot = slot;
        _isEquipped = true;
    }

    public virtual void OnUnequipped()
    {
        _equippedSlot = null;
        _isEquipped = false;
    }

    public virtual void OnUpdate(double delta)
    {
        // Override in derived classes
    }

    public virtual void OnPrimaryAction()
    {
        // Override in derived classes
    }

    public virtual void OnPrimaryActionRelease()
    {
        // Override in derived classes
    }

    public virtual void OnSecondaryAction()
    {
        // Override in derived classes
    }

    public virtual void OnSecondaryActionRelease()
    {
        // Override in derived classes
    }

    /// <summary>
    /// Get the hand side this tool is equipped to.
    /// </summary>
    protected HandSide GetHandSide()
    {
        return _equippedSlot?.Hand ?? HandSide.Right;
    }

    /// <summary>
    /// Get the global transform of the tool slot.
    /// </summary>
    protected Transform3D GetSlotTransform()
    {
        return _equippedSlot?.GlobalTransform ?? Transform3D.Identity;
    }
}
