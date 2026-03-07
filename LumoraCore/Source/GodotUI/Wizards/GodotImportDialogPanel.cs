using Lumora.Core.Assets;
using Lumora.Core.Components;
using Lumora.Core.Math;

namespace Lumora.Core.GodotUI.Wizards;

/// <summary>
/// In-world import dialog panel used by clipboard/file paste flows.
/// </summary>
[ComponentCategory("GodotUI/Wizards")]
public sealed class GodotImportDialogPanel : GodotUIPanel
{
    protected override string DefaultScenePath => LumAssets.UI.ImportDialog;
    protected override float2 DefaultSize => new float2(320, 260);
    protected override float DefaultPixelsPerUnit => 900f;
    protected override float DefaultRefreshRate => 0f;

    /// <summary>
    /// Source file path being imported.
    /// </summary>
    public Sync<string> FilePath { get; private set; } = null!;

    /// <summary>
    /// Target slot for the import operation.
    /// </summary>
    public SyncRef<Slot> TargetSlot { get; private set; } = null!;

    /// <summary>
    /// Whether closing/hiding the dialog should destroy the panel slot.
    /// </summary>
    public Sync<bool> AutoDestroyOnClose { get; private set; } = null!;

    /// <summary>
    /// Local asset DB used for import persistence.
    /// Non-synced runtime reference.
    /// </summary>
    public LocalDB? LocalDB { get; private set; }

    public override void OnAwake()
    {
        base.OnAwake();

        FilePath = new Sync<string>(this, string.Empty);
        TargetSlot = new SyncRef<Slot>(this);
        AutoDestroyOnClose = new Sync<bool>(this, true);

        FilePath.OnChanged += _ => NotifyChanged();
        TargetSlot.OnChanged += _ => NotifyChanged();
    }

    public override void OnAttach()
    {
        base.OnAttach();

        // Import dialogs should always be movable in-world.
        if (Slot.GetComponent<Grabbable>() == null)
        {
            Slot.AttachComponent<Grabbable>();
        }
    }

    public void Configure(string filePath, Slot targetSlot, LocalDB? localDB)
    {
        FilePath.Value = filePath ?? string.Empty;
        TargetSlot.Target = targetSlot;
        LocalDB = localDB;
        NotifyChanged();
    }
}
