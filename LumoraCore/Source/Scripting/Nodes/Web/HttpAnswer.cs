// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// What a web request came back with: the status (0 when nothing came back), the body as text, the response
// headers one "Name: value" per line, the address it ended at after redirects, and what went wrong, if
// anything did.
public readonly record struct HttpAnswer(int Status, string Content, string ResponseHeaders, string FinalUrl, string Error);
