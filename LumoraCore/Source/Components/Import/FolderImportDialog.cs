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
// flow is just a vertical list of registry entries (no wizard/back/grid). - xlinka
[ComponentCategory("Assets/Import")]
public sealed class FolderImportDialog : Component
{
    public readonly Sync<string> Path;

    public World? TargetWorld { get; set; }

    private User? _importingUser;
    private PanelShell? _panel;
    private FontProvider? _fontProvider;

    private bool CanInteract => _importingUser == null || _importingUser == World?.LocalUser;

    public FolderImportDialog()
    {
        Path = new Sync<string>(this, string.Empty);
    }

    public void SetLocalUserAsImporting()
    {
        if (_importingUser != null)
            throw new InvalidOperationException("Importing user is already set!");
        _importingUser = World.LocalUser;
    }

    public override void OnStart()
    {
        base.OnStart();
        EnsureBuilt();
    }

    private void EnsureBuilt()
    {
        if (_panel != null) return;

        var canvasSize = new float2(280f, 540f);
        var canvasScale = 0.5f / canvasSize.y;
        Slot.LocalScale.Value = new float3(canvasScale, canvasScale, canvasScale);

        _panel = Slot.GetComponent<PanelShell>() ?? Slot.AttachComponent<PanelShell>();
        _panel.Title.Value = "Folder Import";
        _panel.Size.Value = canvasSize;

        if (ImportDialog.DefaultFontUrl != null)
        {
            var fontSlot = Slot.AddSlot("DialogFont");
            _fontProvider = fontSlot.AttachComponent<FontProvider>();
            _fontProvider.URL.Value = ImportDialog.DefaultFontUrl;
            _fontProvider.FallbackURLs.Add(ImportDialog.DefaultFontUrl);
            _panel.Font.Target = _fontProvider;
        }

        var content = _panel.ContentSlot!;
        var ui = new UIBuilder(content);
        if (_fontProvider != null) ui.Font(_fontProvider);
        ui.FontSize(14f);
        ui.ScrollRect(out _);
        ui.VerticalLayout(4f, 6f);

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
            ui.PushStyle().MinHeight(36f).PreferredHeight(36f);
            ui.Button(importers[captured].Name, (_, _) => RunImport(captured), ImportDialog.ButtonFill);
            ui.PopStyle();
        }
    }

    private void RunImport(int index)
    {
        if (!CanInteract) return;
        var info = FolderImporter.FolderImporters[index];
        var folderPath = Path.Value;

        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            Logger.Warn($"FolderImportDialog: invalid path '{folderPath}'");
            return;
        }

        var parent = (TargetWorld ?? World).RootSlot;
        var spawnName = System.IO.Path.GetFileName(folderPath);
        if (string.IsNullOrEmpty(spawnName)) spawnName = folderPath;
        var spawnSlot = parent.AddSlot(spawnName);
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
