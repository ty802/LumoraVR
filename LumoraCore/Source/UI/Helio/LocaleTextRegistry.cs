// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Localization;

namespace Helio.UI;

// Every Text built from a KEYED value is remembered here with the value it came from, so switching
// language rewrites exactly those strings and nothing else.
//
// Why a registry and not a rebuild: the alternative was to raise a "locale changed" and have each
// screen tear its content down and build it again. That throws away scroll position, an in-flight
// rebind capture and the listing views' realized rows, and it re-tessellates every canvas whether or
// not a single string on it was translatable. This walks a list once per language switch - which
// happens maybe once in a session - writes only the strings that actually differ (an equality gate,
// same reason the listing templates have one), and dirties each affected canvas exactly once.
//
// A plain string never lands here. Passing one costs a struct copy and a branch, so raw-string call
// sites pay nothing for the machinery existing. -xlinka
public static class LocaleTextRegistry
{
    // Destroyed panels leave dead entries behind. Sweeping on every registration would be quadratic on
    // a screen build, so the sweep waits until the table has grown past the last swept size by this
    // much. A locale change always sweeps.
    private const int SweepGrowth = 256;

    private static readonly Dictionary<Text, LocaleText> _live = new();
    private static readonly List<Text> _stale = new();
    private static bool _hooked;
    private static int _sweptAt;

    public static int LiveCount
    {
        get
        {
            lock (_live)
                return _live.Count;
        }
    }

    // Resolve now, and keep it resolving. Returns the same Text so this can wrap a builder call.
    public static Text Bind(Text? text, in LocaleText source)
    {
        if (text == null || text.IsDestroyed)
            return text!;

        string resolved = source.Resolve();
        if (text.Content.Value != resolved)
            text.Content.Value = resolved;

        if (!source.IsKey)
        {
            // Nothing to re-resolve, and a value that used to be keyed must not keep its old entry.
            Unbind(text);
            return text;
        }

        lock (_live)
        {
            EnsureHooked();
            _live[text] = source;
            if (_live.Count - _sweptAt >= SweepGrowth)
                Sweep();
        }
        return text;
    }

    public static void Unbind(Text? text)
    {
        if (text == null)
            return;
        lock (_live)
        {
            if (_live.Remove(text) && _sweptAt > _live.Count)
                _sweptAt = _live.Count;
        }
    }

    public static void Clear()
    {
        lock (_live)
        {
            _live.Clear();
            _sweptAt = 0;
        }
    }

    // Rewrite every registered string against the active locale. Public so a harness can drive it
    // without going through the event.
    public static void Reresolve()
    {
        HashSet<Canvas>? dirty = null;
        lock (_live)
        {
            Sweep();
            foreach (var pair in _live)
            {
                var text = pair.Key;
                string resolved = pair.Value.Resolve();
                if (text.Content.Value == resolved)
                    continue;
                text.Content.Value = resolved;
                var canvas = text.Slot?.GetComponentInParents<Canvas>();
                if (canvas == null || canvas.IsDestroyed)
                    continue;
                dirty ??= new HashSet<Canvas>();
                dirty.Add(canvas);
            }
        }

        if (dirty == null)
            return;
        // Outside the lock: a layout pass can build UI, and building UI can register text.
        foreach (var canvas in dirty)
            canvas.MarkLayoutDirty();
    }

    private static void EnsureHooked()
    {
        if (_hooked)
            return;
        _hooked = true;
        LocaleManager.Changed += Reresolve;
    }

    private static void Sweep()
    {
        _stale.Clear();
        foreach (var pair in _live)
        {
            var text = pair.Key;
            if (text.IsDestroyed || text.Slot == null || text.Slot.IsDestroyed)
                _stale.Add(text);
        }
        for (int i = 0; i < _stale.Count; i++)
            _live.Remove(_stale[i]);
        _stale.Clear();
        _sweptAt = _live.Count;
    }
}
