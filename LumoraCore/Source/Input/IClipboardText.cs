// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Input;

// Read/write access to the OS clipboard's TEXT, injected by the platform layer at startup.
//
// This is a different thing from IClipboardPasteHandler, which pulls FILES and IMAGES off the
// clipboard and pushes them through the asset import pipeline. That one hands back nothing a text
// field could take, which is why copy/paste on a field needed its own service instead of borrowing
// it. A platform with no clipboard leaves InputInterface.ClipboardText null and every caller
// degrades to a no-op rather than pretending. -xlinka
public interface IClipboardText
{
    string GetText();

    void SetText(string text);
}
