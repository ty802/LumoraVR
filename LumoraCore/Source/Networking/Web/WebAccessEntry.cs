// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Networking.Web;

// A decided host, as Settings lists it.
public readonly record struct WebAccessEntry(string Host, bool Allowed);
