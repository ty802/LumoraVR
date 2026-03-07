using System;

namespace Lumora.Core.Components.UI;

/// <summary>
/// Base component that contributes items to the context menu when it opens.
///
/// Attach to any slot in the world or user hierarchy.
/// The ContextMenuSystem collects all active ContextMenuItemSource components
/// and calls PopulateContextMenu() on each one when the menu opens.
///
/// Example usage:
/// <code>
///   public class GrabTool : ContextMenuItemSource
///   {
///       public override void PopulateContextMenu(ContextMenuPage page)
///       {
///           page.AddItem("Duplicate", _ => Duplicate(), "res://Icons/duplicate.png");
///           page.AddItem("Delete",    _ => Delete(),    "res://Icons/delete.png");
///       }
///   }
/// </code>
/// </summary>
[ComponentCategory("UI/Context Menu")]
public class ContextMenuItemSource : Component
{
    /// <summary>
    /// Priority for ordering. Higher priority sources run first (their items appear first).
    /// </summary>
    public Sync<int> Priority { get; private set; }

    public override void OnAwake()
    {
        base.OnAwake();
        Priority = new Sync<int>(this, 0);
    }

    /// <summary>
    /// Override to add items to the context menu page.
    /// Called each time the context menu opens (before it is shown).
    /// Items already added by higher-priority sources are visible in page.Items.
    /// </summary>
    public virtual void PopulateContextMenu(ContextMenuPage page) { }
}
