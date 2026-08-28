// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Lumora.Core.Logging;

namespace Lumora.Core.Input.Actions;

// Reads and writes the user's binding overrides as JSON.
//
// Only actions the user actually changed are written. That is not a size optimization: it means a
// later build can move a default (fix a bad shipped binding, add a gamepad column) and everybody
// picks it up, while the three keys somebody deliberately remapped stay remapped. An action that
// was deliberately CLEARED is written as an empty list, which is why "absent" and "empty" have to
// stay distinguishable.
//
// Control ids are written out longhand ("Keyboard/W", "Gamepad/FaceDown") rather than as numbers,
// so the file is readable and survives us reordering an enum. -xlinka
public static class InputBindingStore
{
    public const string SettingsKey = "Engine.Input.Bindings";

    public static string Serialize(InputBindingMap map)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", InputBindingMap.MapVersion);
            writer.WriteStartObject("actions");

            foreach (var action in map.AllActions())
            {
                if (!action.IsOverridden)
                    continue;

                writer.WriteStartArray(action.Path);
                foreach (var binding in action.Bindings)
                    WriteBinding(writer, binding);
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteBinding(Utf8JsonWriter writer, InputBinding binding)
    {
        writer.WriteStartObject();
        writer.WriteString("control", binding.Control.Serialize());
        if (binding.Source != BindingAxis.Auto)
            writer.WriteString("source", binding.Source.ToString());
        if (binding.Target != BindingAxis.Auto)
            writer.WriteString("target", binding.Target.ToString());
        if (binding.Scale != 1f)
            writer.WriteNumber("scale", binding.Scale);
        if (binding.Modifiers.Count > 0)
        {
            writer.WriteStartArray("modifiers");
            foreach (var modifier in binding.Modifiers)
                writer.WriteStringValue(modifier.Serialize());
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }

    // Layer stored overrides onto a map that already holds the shipped defaults. Anything malformed
    // is skipped and the default is kept: a corrupt binding file must never leave someone unable to
    // walk.
    public static void Apply(InputBindingMap map, string? json)
    {
        if (map == null || string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return;

            if (root.TryGetProperty("version", out var version)
                && version.ValueKind == JsonValueKind.Number
                && version.GetInt32() > InputBindingMap.MapVersion)
            {
                Logger.Warn($"InputBindingStore: stored bindings are version {version.GetInt32()}, this build understands {InputBindingMap.MapVersion}; using defaults.");
                return;
            }

            if (!root.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Object)
                return;

            int applied = 0;
            foreach (var entry in actions.EnumerateObject())
            {
                var action = map.Find(entry.Name);
                if (action == null || entry.Value.ValueKind != JsonValueKind.Array)
                    continue;

                var parsed = new List<InputBinding>();
                bool malformed = false;
                foreach (var element in entry.Value.EnumerateArray())
                {
                    if (!TryReadBinding(element, out var binding))
                    {
                        malformed = true;
                        break;
                    }
                    parsed.Add(binding);
                }
                if (malformed)
                    continue;

                action.ClearBindings();
                foreach (var binding in parsed)
                    action.Bind(binding);
                action.IsOverridden = true;
                applied++;
            }

            if (applied > 0)
                Logger.Log($"InputBindingStore: applied {applied} rebound action(s).");
        }
        catch (Exception ex)
        {
            Logger.Warn($"InputBindingStore: failed to read stored bindings, keeping defaults: {ex.Message}");
        }
    }

    private static bool TryReadBinding(JsonElement element, out InputBinding binding)
    {
        binding = null!;
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        if (!element.TryGetProperty("control", out var controlText)
            || controlText.ValueKind != JsonValueKind.String
            || !ControlRef.TryParse(controlText.GetString(), out var control))
            return false;

        var source = ReadAxis(element, "source");
        var target = ReadAxis(element, "target");

        float scale = 1f;
        if (element.TryGetProperty("scale", out var scaleValue) && scaleValue.ValueKind == JsonValueKind.Number)
            scale = scaleValue.GetSingle();

        List<ControlRef>? modifiers = null;
        if (element.TryGetProperty("modifiers", out var modifierArray) && modifierArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var modifier in modifierArray.EnumerateArray())
            {
                if (modifier.ValueKind != JsonValueKind.String || !ControlRef.TryParse(modifier.GetString(), out var parsedModifier))
                    return false;
                modifiers ??= new List<ControlRef>();
                modifiers.Add(parsedModifier);
            }
        }

        binding = new InputBinding(control, source, target, scale, modifiers);
        return true;
    }

    private static BindingAxis ReadAxis(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && Enum.TryParse<BindingAxis>(value.GetString(), ignoreCase: true, out var axis))
            return axis;
        return BindingAxis.Auto;
    }
}
