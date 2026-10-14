// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// One Delay node: the export that runs what comes after it, which takes the word of the waiting Function's
// call it was armed in when it carries one.
public sealed record ScriptTimerEntry(string NodeId, string Export, bool CarriesWord = false);
