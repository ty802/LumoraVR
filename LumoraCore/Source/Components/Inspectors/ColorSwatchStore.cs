// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Lumora.Core.Logging;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// The color picker's per-user palette: a hand-curated Saved list and an automatic Recent trail.
//
// Both live in the engine settings database under their own keys, as JSON arrays of #RRGGBBAA
// strings, through the same slot API the input bindings use. Written on every change rather than on
// exit, because a swatch you pinned and then lost to a crash is worse than no swatch feature at all.
//
// The lists are held IN MEMORY and only mirrored out to settings. A headless run with no writable
// profile still gets working swatches for the session, which is what keeps the picker testable
// outside a real client. Nothing here throws at the caller either: a corrupt or unreachable store
// degrades to an empty palette, never to a picker that refuses to open. -xlinka
public static class ColorSwatchStore
{
    public const string SavedKey = "Engine.Color.Swatches";
    public const string RecentKey = "Engine.Color.Recent";

    // Recent is a trail, not a palette. Eight fits one grid row plus a bit, and past that you are
    // reaching for the Saved list anyway.
    public const int RecentLimit = 8;
    // Hard ceiling on Saved so a stuck plus button cannot grow the settings blob without bound.
    public const int SavedLimit = 96;

    private static readonly object _lock = new();
    private static List<color>? _saved;
    private static List<color>? _recent;

    public static IReadOnlyList<color> Saved
    {
        get { lock (_lock) { EnsureLoaded(); return _saved!.ToArray(); } }
    }

    public static IReadOnlyList<color> Recent
    {
        get { lock (_lock) { EnsureLoaded(); return _recent!.ToArray(); } }
    }

    // Pin a color. A duplicate is refused rather than reordered: the Saved grid is a layout the user
    // arranged, and a chip jumping to the end because they re-added it would scramble it.
    public static bool AddSaved(in color value)
    {
        lock (_lock)
        {
            EnsureLoaded();
            if (IndexOf(_saved!, value) >= 0 || _saved!.Count >= SavedLimit)
                return false;
            _saved.Add(value);
            Write(SavedKey, _saved);
            return true;
        }
    }

    public static bool RemoveSaved(int index)
    {
        lock (_lock)
        {
            EnsureLoaded();
            if (index < 0 || index >= _saved!.Count)
                return false;
            _saved.RemoveAt(index);
            Write(SavedKey, _saved);
            return true;
        }
    }

    // Newest first, no repeats: re-using a color moves it to the front instead of stacking up.
    public static void PushRecent(in color value)
    {
        lock (_lock)
        {
            EnsureLoaded();
            int existing = IndexOf(_recent!, value);
            if (existing == 0)
                return;
            if (existing > 0)
                _recent!.RemoveAt(existing);
            _recent!.Insert(0, value);
            while (_recent.Count > RecentLimit)
                _recent.RemoveAt(_recent.Count - 1);
            Write(RecentKey, _recent);
        }
    }

    // Drop the cache so the next read comes off the settings store again.
    public static void Reload()
    {
        lock (_lock)
        {
            _saved = null;
            _recent = null;
        }
    }

    // Rounds to the nearest byte, so a color that came out of a hex string goes back out as the same
    // hex string. Truncating instead drifts a channel down by one on every round trip.
    public static string FormatHex(in color value)
        => $"#{ToByte(value.r):X2}{ToByte(value.g):X2}{ToByte(value.b):X2}{ToByte(value.a):X2}";

    public static string FormatHexShort(in color value)
        => value.a >= 0.999f
            ? $"#{ToByte(value.r):X2}{ToByte(value.g):X2}{ToByte(value.b):X2}"
            : FormatHex(value);

    // #RRGGBB (opaque) or #RRGGBBAA, hash optional.
    public static bool TryParseHex(string? text, out color value)
    {
        value = color.White;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string digits = text.Trim().TrimStart('#');
        if (digits.Length != 6 && digits.Length != 8)
            return false;
        if (!uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint packed))
            return false;

        float a = 1f;
        if (digits.Length == 8)
        {
            a = (packed & 0xFF) / 255f;
            packed >>= 8;
        }
        value = new color(
            ((packed >> 16) & 0xFF) / 255f,
            ((packed >> 8) & 0xFF) / 255f,
            (packed & 0xFF) / 255f,
            a);
        return true;
    }

    public static int ToByte(float v)
    {
        int b = (int)MathF.Round(v * 255f);
        return b < 0 ? 0 : (b > 255 ? 255 : b);
    }

    // Byte-space comparison, not float equality: two colors that render identically and serialize to
    // the same hex are the same swatch, or the plus button happily stores visual duplicates.
    private static int IndexOf(List<color> list, in color value)
    {
        string hex = FormatHex(value);
        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(FormatHex(list[i]), hex, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    private static void EnsureLoaded()
    {
        _saved ??= Read(SavedKey, SavedLimit);
        _recent ??= Read(RecentKey, RecentLimit);
    }

    private static List<color> Read(string key, int limit)
    {
        var list = new List<color>();
        string json;
        try
        {
            json = Settings.ReadValue<string>(key, string.Empty) ?? string.Empty;
        }
        catch (Exception ex)
        {
            Logger.Warn($"ColorSwatchStore: failed to read {key}: {ex.Message}");
            return list;
        }
        if (string.IsNullOrWhiteSpace(json))
            return list;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return list;
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String)
                    continue;
                if (list.Count < limit && TryParseHex(entry.GetString(), out var parsed))
                    list.Add(parsed);
            }
        }
        catch (Exception ex)
        {
            // A mangled palette costs you your swatches and nothing else.
            Logger.Warn($"ColorSwatchStore: failed to parse {key}: {ex.Message}");
            list.Clear();
        }
        return list;
    }

    private static void Write(string key, List<color> list)
    {
        try
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartArray();
                foreach (var entry in list)
                    writer.WriteStringValue(FormatHex(entry));
                writer.WriteEndArray();
            }
            Settings.WriteValue(key, Encoding.UTF8.GetString(buffer.ToArray()));
        }
        catch (Exception ex)
        {
            // No writable profile (headless, locked-down box): the in-memory list still stands for
            // the rest of the session.
            Logger.Warn($"ColorSwatchStore: failed to write {key}: {ex.Message}");
        }
    }
}
