// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Scripting;

namespace Lumora.Core.Scripting;

// Messages between one author's scripts: from a Shared script to every machine's copy of that author's
// Local scripts on an object, and from any of that author's scripts to their Shared scripts on an object
// on the host. Only between scripts of one author, so no one's script speaks for someone else's. A text
// and a number. -xlinka
internal static class ScriptMessages
{
    public static string Text(string? message) => message ?? string.Empty;

    public static void ToEveryMachine(ScriptPrincipal sender, Slot target, string message, float value)
    {
        foreach (var host in target.GetComponents<ScriptHost>())
        {
            if (host.Mode.Value == ScriptMode.Local && SameAuthor(host, sender))
                host.Post(message, value);
        }
    }

    public static void ToHost(ScriptPrincipal sender, Slot target, string message, float value, User? from)
    {
        foreach (var host in target.GetComponents<ScriptHost>())
        {
            if (host.Mode.Value == ScriptMode.Shared && SameAuthor(host, sender))
                host.Receive(message, value, from);
        }
    }

    private static bool SameAuthor(ScriptHost host, ScriptPrincipal sender)
        => !host.IsDestroyed && ScriptPrincipal.ForElement(host) is { } author && author.AuthorKey == sender.AuthorKey;
}
