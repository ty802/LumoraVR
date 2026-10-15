// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Assets.Interop.Unity;

// How a VRCFury component keeps its settings: the feature and every action it holds are entries in one
// reference list (references.RefIds), each a class name plus its fields, pointing at each other by id.
// A feature's state is a list of those ids. -xlinka
public static class UnityVrcFury
{
    public readonly record struct Entry(string Class, UnityNode Data);

    public static Dictionary<string, Entry> References(UnityDocument doc)
    {
        var map = new Dictionary<string, Entry>(StringComparer.Ordinal);
        if (doc["references"]?["RefIds"] is not UnitySeq refs)
            return map;
        foreach (var item in refs.Items)
        {
            var rid = item["rid"]?.Text;
            var name = item["type"]?["class"]?.Text;
            if (rid != null && name != null && item["data"] is { } data)
                map[rid] = new Entry(name, data);
        }
        return map;
    }

    // The actions of a state ("state: actions: - rid: ..."), in order.
    public static IEnumerable<Entry> Actions(UnityNode? state, Dictionary<string, Entry> references)
    {
        if (state?["actions"] is not UnitySeq actions)
            yield break;
        foreach (var item in actions.Items)
        {
            if (item["rid"]?.Text is { } rid && references.TryGetValue(rid, out var entry))
                yield return entry;
        }
    }

    // Every action under a state, flipbook pages included.
    public static IEnumerable<Entry> AllActions(UnityNode? state, Dictionary<string, Entry> references, int depth = 0)
    {
        foreach (var action in Actions(state, references))
        {
            yield return action;
            if (depth < 4 && action.Class == "FlipBookBuilderAction" && action.Data["pages"] is UnitySeq pages)
                foreach (var page in pages.Items)
                    foreach (var inner in AllActions(page["state"], references, depth + 1))
                        yield return inner;
        }
    }

    // A wrapped asset reference ({objRef: {fileID, guid}, id: "guid|path"}): its GUID.
    public static string? Guid(UnityNode? wrapped)
    {
        if (wrapped == null)
            return null;
        var guid = wrapped["objRef"]?.Ref.Guid;
        if (!string.IsNullOrEmpty(guid))
            return guid;
        var id = wrapped["id"]?.Text;
        if (string.IsNullOrEmpty(id))
            return null;
        int bar = id.IndexOf('|');
        return bar > 0 ? id[..bar] : null;
    }
}
