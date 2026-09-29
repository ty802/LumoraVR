// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Networking.Web;

// Headers is one "Name: value" per line. Body is sent as UTF-8 for POST, PUT, PATCH and DELETE and
// ignored for the rest. TimeoutSeconds 0 means the default.
public sealed record ScriptWebRequest(Uri Url, WebMethod Method, string? Headers, string? Body, float TimeoutSeconds);
