// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Diagnostics;
using System.IO;
using Lumora.Core.Localization;
using Lumora.Core.Logging;

namespace Lumora.Core.Components.UI;

[ComponentCategory("Hidden")]
public sealed class OpenLogsWidgetPreset : ActionWidgetPreset
{
    protected override string Title => "Widget.Logs.Title".AsLocale("Open logs folder").Resolve();
    protected override string? Icon => "files";
    protected override string? Unavailable => Engine.IsMobilePlatform
        ? "Widget.Logs.DesktopOnly".AsLocale("Available on desktop").Resolve() : null;

    protected override void Invoke()
    {
        if (Engine.IsMobilePlatform)
            return;

        string directory = Logger.LogDirectory;
        try
        {
            // The logger can fall back to a temporary folder if its normal location is unwritable.
            directory = Path.GetDirectoryName(Logger.LogFile) ?? directory;
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Warn($"OpenLogsWidget: could not open '{directory}': {ex.Message}");
            ModalHost.For(Slot)?.Open(new ModalRequest
            {
                Title = Title,
                Message = "Widget.Logs.OpenFailed".AsLocale("Could not open the log folder:\n{0}", directory),
                Invoker = Slot,
            });
        }
    }
}
