// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Lumora.Script;

// Text read into a JSON value. Comments and a comma before a closing bracket are let through; a key given
// twice keeps its last value in its first place. Nesting is bounded by the parser itself, the length before
// it starts and the count of values as they are made, so hostile text is turned away before it grows.
public static class ScriptJsonReader
{
    public static bool TryRead(ScriptCollectionBudget budget, string? text, out ScriptJson? value, out string error)
    {
        value = null;
        error = string.Empty;
        if (text == null)
        {
            error = "no text";
            return false;
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "the text holds no JSON";
            return false;
        }

        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text), new JsonReaderOptions
        {
            MaxDepth = ScriptJson.MaxDepth,
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });
        try
        {
            if (!reader.Read())
            {
                error = "the text holds no JSON";
                return false;
            }
            var read = Read(ref reader, budget);
            // Anything but space and comments after the value is an error the reader raises here.
            while (reader.Read())
            {
            }
            value = read;
            return true;
        }
        catch (JsonException ex)
        {
            error = Describe(ex);
            return false;
        }
        // Text the reader cannot turn into a string, such as an escaped half of a surrogate pair.
        catch (InvalidOperationException ex)
        {
            error = ex.Message.Length > 256 ? ex.Message[..256] : ex.Message;
            return false;
        }
    }

    private static ScriptJson Read(ref Utf8JsonReader reader, ScriptCollectionBudget budget)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
            {
                var members = new List<KeyValuePair<string, ScriptJson>>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    string key = reader.GetString()!;
                    reader.Read();
                    members.Add(new KeyValuePair<string, ScriptJson>(key, Read(ref reader, budget)));
                }
                return ScriptJsonObject.Of(budget, members);
            }
            case JsonTokenType.StartArray:
            {
                var items = new List<ScriptJson>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    items.Add(Read(ref reader, budget));
                return ScriptJsonArray.Of(budget, items);
            }
            case JsonTokenType.String:
                return ScriptJsonValue.Of(budget, reader.GetString());
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out long whole))
                    return ScriptJsonValue.Of(budget, whole);
                if (reader.TryGetUInt64(out ulong large))
                    return ScriptJsonValue.Of(budget, large);
                if (reader.TryGetDouble(out double number) && double.IsFinite(number))
                    return ScriptJsonValue.Of(budget, number);
                throw new JsonException($"{Encoding.UTF8.GetString(reader.ValueSpan)} is too big for a number", null,
                    null, reader.BytesConsumed);
            case JsonTokenType.True:
                return ScriptJsonValue.True;
            case JsonTokenType.False:
                return ScriptJsonValue.False;
            default:
                return ScriptJsonValue.Null;
        }
    }

    // The reader's own words without its position suffix, and the position as a person counts it.
    private static string Describe(JsonException ex)
    {
        var message = ex.Message;
        int cut = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
        if (cut < 0)
            cut = message.IndexOf(" Path:", StringComparison.Ordinal);
        if (cut >= 0)
            message = message[..cut];
        if (ex.LineNumber is long line && ex.BytePositionInLine is long column)
            message += $" (line {line + 1}, position {column + 1})";
        return message.Length > 256 ? message[..256] : message;
    }
}
