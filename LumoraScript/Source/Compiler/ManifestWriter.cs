// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Lumora.Script;

// The "lumora.manifest" custom section: what a module needs and offers, readable without running it.
// Events and their payloads, the Delay continuations, the awaits and what their answers carry, every
// import with its capability, the binding
// slots, the Displays and Pulses, the stored variables, and the hash of the graph it was built from.
internal static class ManifestWriter
{
    public const string FormatName = "loom.manifest/1";

    public static byte[] Write(string graphHash, IReadOnlyList<ScriptEventEntry> events, IReadOnlyList<ScriptTimerEntry> timers,
        IReadOnlyList<ScriptAwaitEntry> awaits, IReadOnlyList<NodeOverload> imports,
        IReadOnlyList<ScriptBindingEntry> bindings, IReadOnlyList<ScriptVariableEntry> variables,
        IReadOnlyList<ScriptDisplayEntry> displays)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("format", FormatName);
            writer.WriteString("graph", graphHash);

            writer.WriteStartArray("events");
            foreach (var entry in events)
            {
                writer.WriteStartObject();
                writer.WriteString("node", entry.NodeId);
                writer.WriteString("type", entry.EventName);
                writer.WriteString("export", entry.Export);
                if (entry.SourceBinding >= 0)
                    writer.WriteNumber("source", entry.SourceBinding);
                if (entry.IntervalSeconds > 0f)
                    writer.WriteNumber("every", entry.IntervalSeconds);
                writer.WriteStartArray("payload");
                foreach (var port in entry.Overload.Outputs)
                    writer.WriteStringValue(port.Type.Name);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("timers");
            foreach (var timer in timers)
            {
                writer.WriteStartObject();
                writer.WriteString("node", timer.NodeId);
                writer.WriteString("export", timer.Export);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("awaits");
            foreach (var entry in awaits)
            {
                writer.WriteStartObject();
                writer.WriteString("node", entry.NodeId);
                writer.WriteStartArray("answers");
                for (int i = 0; i < entry.Exits.Count; i++)
                {
                    writer.WriteStartObject();
                    writer.WriteString("exit", entry.Exits[i]);
                    writer.WriteString("export", entry.Exports[i]);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteStartArray("payload");
                foreach (var port in entry.Overload.Outputs)
                    writer.WriteStringValue(port.Type.Name);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("imports");
            foreach (var overload in imports)
            {
                writer.WriteStartObject();
                writer.WriteString("name", overload.ImportName);
                writer.WriteString("capability", overload.Definition.Capability.ToString());
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("bindings");
            foreach (var binding in bindings)
            {
                writer.WriteStartObject();
                writer.WriteString("name", binding.Name);
                writer.WriteString("type", binding.Type.Name);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("displays");
            foreach (var display in displays)
            {
                writer.WriteStartObject();
                writer.WriteString("node", display.NodeId);
                writer.WriteBoolean("counts", display.CountsRuns);
                if (display.Export != null)
                    writer.WriteString("export", display.Export);
                writer.WriteBoolean("captured", display.Captured);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("variables");
            foreach (var variable in variables)
            {
                writer.WriteStartObject();
                writer.WriteString("node", variable.NodeId);
                writer.WriteString("name", variable.Name);
                writer.WriteString("type", variable.Type.Name);
                writer.WriteStartArray("exports");
                foreach (var export in variable.Exports)
                    writer.WriteStringValue(export);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}
