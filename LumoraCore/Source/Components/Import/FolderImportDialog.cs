// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.UI;
using Lumora.Core.Logging;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Import;

// Lightweight single-panel dialog - doesn't inherit from ImportDialog because the
// flow is just a vertical list of registry entries (no wizard/back/grid). Stands out
// in the world like the other import dialogs and follows their rules. - xlinka
[ComponentCategory("Assets/Import")]
public sealed class FolderImportDialog : Component
{
    // A folder on the importer's machine. Never synced: nobody else could open it.
    public string Path { get; set; } = string.Empty;

    public World? TargetWorld { get; set; }

    // Who opened the dialog, synced so every machine knows whose it is; null on a dialog nobody claimed.
    public readonly SyncRef<User> ImportingUser = new();

    private bool _attachedHere;
    private PanelShell? _panel;
    private FontProvider? _fontProvider;

    private bool CanInteract => IsExecutionAllowed && ImportDialog.MayUse(ImportingUser, World);

    public void SetLocalUserAsImporting()
    {
        if (!ImportingUser.Value.IsNull)
            throw new InvalidOperationException("Importing user is already set!");
        ImportingUser.Target = World.LocalUser;
    }

    public override void OnAttach()
    {
        base.OnAttach();
        _attachedHere = true;
    }

    // Built and run by the machine that made it, as ImportDialog is; one from another machine only shows
    // what its maker built.
    public override void OnStart()
    {
        base.OnStart();
        if (_attachedHere || IsMintedHere)
        {
            if (!_attachedHere || ImportingUser.Value.IsNull)
                ImportingUser.Target = World.LocalUser;
            EnsureBuilt(copied: !_attachedHere);
        }
    }

    public override void OnUserLeft(User user) => ImportDialog.TakeDownForLeaver(this, ImportingUser, user);

    private void EnsureBuilt(bool copied)
    {
        if (_panel != null) return;

        var canvasSize = new float2(460f, 520f);
        var canvasScale = 0.5f / canvasSize.y;
        Slot.LocalScale.Value = new float3(canvasScale, canvasScale, canvasScale);

        _panel = Slot.GetComponent<PanelShell>() ?? Slot.AttachComponent<PanelShell>();
        _panel.Title.Value = "Folder Import";
        _panel.Size.Value = canvasSize;
        if (copied)
        {
            Slot.FindChild("DialogFont", recursive: false)?.Destroy();
            _panel.ClearContent();
        }

        var fontUrl = ImportDialog.ResolveFontUrl(World);
        if (fontUrl != null)
        {
            var fontSlot = Slot.AddSlot("DialogFont");
            _fontProvider = fontSlot.AttachComponent<FontProvider>();
            _fontProvider.URL.Value = fontUrl;
            _fontProvider.FallbackURLs.Add(fontUrl);
            _panel.Font.Target = _fontProvider;
        }

        var content = _panel.ContentSlot!;
        ImportDialog.ClaimCanvas(content, ImportingUser);
        var layout = content.GetComponent<VerticalLayout>() ?? content.AttachComponent<VerticalLayout>();
        layout.PaddingLeft.Value = ImportDialog.GridPadding;
        layout.PaddingRight.Value = ImportDialog.GridPadding;
        layout.PaddingTop.Value = ImportDialog.GridPadding;
        layout.PaddingBottom.Value = ImportDialog.GridPadding;
        layout.ForceExpandHeight.Value = false;
        var ui = new UIBuilder(content);
        if (_fontProvider != null) ui.Font(_fontProvider);
        ui.FontSize(DashTheme.FontBody);
        ui = ImportDialog.ScrollContent(ui);

        if (ImportDialog.SpawnRefusal(TargetWorld ?? World) is { } refusal)
        {
            ShowRefusal(ui, refusal);
            return;
        }

        var importers = FolderImporter.FolderImporters;
        if (importers.Count == 0)
        {
            var none = ui.Text("No folder importers registered.", 14f, ImportDialog.ButtonText);
            none.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
            none.VerticalAlignment.Value = TextVerticalAlignment.Middle;
            return;
        }

        for (int i = 0; i < importers.Count; i++)
        {
            int captured = i;
            ImportDialog.ActionButton(ui, importers[captured].Name, () => RunImport(captured));
        }
    }

    // The reason on the panel in place of the importers, and Close.
    private void ShowRefusal(UIBuilder ui, string refusal)
    {
        var text = ui.Text(refusal, 14f, ImportDialog.ButtonText);
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        text.WordWrap.Value = true;
        ImportDialog.ActionButton(ui, "Close", () => { if (CanInteract) Slot.Destroy(); });
    }

    private void RunImport(int index)
    {
        if (!CanInteract) return;
        if (ImportDialog.SpawnRefusal(TargetWorld ?? World) is { } refusal)
        {
            Logger.Warn($"FolderImportDialog: {refusal} '{Path}' not imported.");
            _panel?.ClearContent();
            var content = _panel?.ContentSlot;
            if (content != null)
            {
                var ui = new UIBuilder(content);
                if (_fontProvider != null) ui.Font(_fontProvider);
                ShowRefusal(ImportDialog.ScrollContent(ui.FontSize(DashTheme.FontBody)), refusal);
            }
            return;
        }
        var info = FolderImporter.FolderImporters[index];
        var folderPath = Path;

        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            Logger.Warn($"FolderImportDialog: invalid path '{folderPath}'");
            return;
        }

        var parent = (TargetWorld ?? World).RootSlot;
        var spawnName = System.IO.Path.GetFileName(folderPath);
        if (string.IsNullOrEmpty(spawnName)) spawnName = folderPath;
        var spawnSlot = parent.World.AddSpawnSlot(parent, spawnName);
        spawnSlot.GlobalPosition = Slot.GlobalPosition;
        spawnSlot.GlobalRotation = Slot.GlobalRotation;
        spawnSlot.GlobalScale = Slot.GlobalScale;

        try
        {
            info.ImportMethod(spawnSlot, folderPath);
        }
        catch (Exception ex)
        {
            Logger.Error($"FolderImportDialog: importer '{info.Name}' threw: {ex}");
        }

        Slot.Destroy();
    }
}
