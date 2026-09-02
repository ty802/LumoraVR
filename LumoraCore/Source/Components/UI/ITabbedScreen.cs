// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.UI;

// A dash screen with sub-tabs that can be raised by name from outside (the capture harness, a deep
// link). False when no tab has that name.
public interface ITabbedScreen
{
    bool ShowTab(string name);
}
