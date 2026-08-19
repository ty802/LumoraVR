// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Nexus.Cloud;

public static class SessionIdentifier
{
    public const string SessionPrefix = "S-";

    public static string Generate()
    {
        return SessionPrefix + Guid.NewGuid().ToString();
    }

    public static string FromGuid(Guid guid)
    {
        return SessionPrefix + guid.ToString();
    }

    public static bool IsValid(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return false;

        if (!sessionId.StartsWith(SessionPrefix))
            return false;

        string guidPart = sessionId.Substring(SessionPrefix.Length);
        return Guid.TryParse(guidPart, out _);
    }

    public static string Normalize(string sessionId)
    {
        return (sessionId?.ToLowerInvariant()) ?? null!;
    }

    public static bool TryGetGuid(string sessionId, out Guid guid)
    {
        guid = Guid.Empty;

        if (!IsValid(sessionId))
            return false;

        string guidPart = sessionId.Substring(SessionPrefix.Length);
        return Guid.TryParse(guidPart, out guid);
    }

    public static bool AreEqual(string sessionId1, string sessionId2)
    {
        if (sessionId1 == null || sessionId2 == null)
            return sessionId1 == sessionId2;

        return string.Equals(sessionId1, sessionId2, StringComparison.OrdinalIgnoreCase);
    }
}
