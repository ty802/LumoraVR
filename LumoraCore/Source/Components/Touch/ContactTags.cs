// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Components.Touch;

// Contact tags as small numbers. A sender and a receiver meet when they share one; the comparison runs per pair per
// frame, so each name is turned into an id once and every list of them carries a 64 bit mask for the quick no.
// Names match exactly, case included.
//
// The body's own tags are fixed: a head, a torso, both hands, both feet and four fingers a side, each sending its
// general name, its side's and, for fingers, which finger. A receiver listening for "Finger" feels any fingertip;
// one listening for "FingerIndexL" feels only that one. -xlinka
public static class ContactTags
{
    private static readonly Dictionary<string, int> Known = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    public static int Id(string tag)
    {
        lock (Gate)
        {
            if (!Known.TryGetValue(tag, out int id))
                Known[tag] = id = Known.Count + 1;
            return id;
        }
    }

    public static int[] Ids(IEnumerable<string> tags)
    {
        var list = new List<int>();
        foreach (var tag in tags)
        {
            if (string.IsNullOrEmpty(tag))
                continue;
            int id = Id(tag);
            if (!list.Contains(id))
                list.Add(id);
        }
        return list.ToArray();
    }

    public static ulong Mask(int[] ids)
    {
        ulong mask = 0;
        foreach (int id in ids)
            mask |= 1UL << (id & 63);
        return mask;
    }

    public static bool Share(int[] a, ulong maskA, int[] b, ulong maskB)
    {
        if ((maskA & maskB) == 0)
            return false;
        foreach (int x in a)
            foreach (int y in b)
                if (x == y)
                    return true;
        return false;
    }

    public static readonly string[] Head = { "Head" };
    public static readonly string[] Torso = { "Torso" };
    public static readonly string[] HandLeft = { "Hand", "HandL" };
    public static readonly string[] HandRight = { "Hand", "HandR" };
    public static readonly string[] FootLeft = { "Foot", "FootL" };
    public static readonly string[] FootRight = { "Foot", "FootR" };

    // Index, middle, ring, little.
    public static readonly string[][] FingersLeft = Fingers("L");
    public static readonly string[][] FingersRight = Fingers("R");

    private static string[][] Fingers(string side)
    {
        var names = new[] { "Index", "Middle", "Ring", "Little" };
        var result = new string[names.Length][];
        for (int i = 0; i < names.Length; i++)
            result[i] = new[] { "Finger", "Finger" + side, "Finger" + names[i], "Finger" + names[i] + side };
        return result;
    }
}
