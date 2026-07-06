// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Import;
using Lumora.Core.Input;
using Lumora.Core.Logging;

namespace Lumora.Core.Components;

/// <summary>
/// World-side Ctrl+V trigger. Detects the paste keystroke (gated on authority +
/// running world) and hands off to the platform clipboard bridge, which reads the
/// real OS clipboard and routes its contents through the import pipeline. The core
/// has no clipboard access of its own, so without a registered bridge a paste is a
/// no-op - it never fabricates placeholder content. - xlinka
/// </summary>
[ComponentCategory("Assets/Import")]
public class ClipboardImporter : Component
{
    private bool _warnedNoBridge;

    private bool CanImport
    {
        get
        {
            if (World == null || !World.IsAuthority)
                return false;

            if (World.State != World.WorldState.Running)
                return false;

            return true;
        }
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        if (!CanImport)
            return;

        var inputInterface = Engine.Current?.InputInterface;
        if (inputInterface == null)
            return;

        // Check for Ctrl+V paste
        if (inputInterface.GetKeyboardDriver() is Keyboard keyboard)
        {
            bool ctrlPressed = keyboard.IsKeyPressed(Key.LeftControl) || keyboard.IsKeyPressed(Key.RightControl);
            bool vPressed = keyboard.IsKeyJustPressed(Key.V);

            if (ctrlPressed && vPressed)
            {
                HandleClipboardPaste();
            }
        }
    }

    private void HandleClipboardPaste()
    {
        var bridge = ImportHandlers.Clipboard;
        if (bridge == null)
        {
            // Honest no-op: the core can't read the OS clipboard, and faking a
            // "pasted content" label would lie about success. The platform layer
            // registers the bridge at startup; if it didn't, paste does nothing.
            if (!_warnedNoBridge)
            {
                Logger.Warn("ClipboardImporter: no platform clipboard bridge registered; Ctrl+V paste is unavailable.");
                _warnedNoBridge = true;
            }
            return;
        }

        Logger.Log("ClipboardImporter: routing Ctrl+V to platform clipboard");
        bridge.Paste();
    }
}
