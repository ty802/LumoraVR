// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Networking.Web;

// Status is the HTTP status for a Response and 0 otherwise. Headers is one "Name: value" per line,
// content headers included. FinalUrl is the last URL requested, after any redirects. Error is empty
// for a Response and one short sentence otherwise.
public sealed record ScriptWebResult(WebOutcome Outcome, int Status, string Body, string Headers, string FinalUrl, string Error)
{
    internal static ScriptWebResult Failed(string url, string error)
        => new(WebOutcome.Failed, 0, string.Empty, string.Empty, url, error);

    internal static ScriptWebResult Denied(string url, string error)
        => new(WebOutcome.Denied, 0, string.Empty, string.Empty, url, error);
}
