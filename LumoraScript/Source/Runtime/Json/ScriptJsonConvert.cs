// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lumora.Script;

// Script values to JSON and back. Every value type has a JSON form: a flag is true or false, a number a
// number, a struct its numbers in lane order as an array (float3 is [x,y,z]), a whole coded type such as a
// byte or a ulong an exact number, and any other coded type what its document form is: a number where it
// has one (a quantity in its base unit), its text otherwise (a date, a guid, an enum's name). Text is a
// string, an address its text, JSON itself, a list an array and a dictionary an object, keys as their text.
// Slots, users and the other world handles have none.
//
// Reading back is forgiving where it can be exact: numbers cross number types (whole ones only into whole
// types, and only in range), text reads in the type's text form, a struct also reads from an object with
// its field names, and anything reads as text. Null reads as nothing, except as JSON. -xlinka
public static class ScriptJsonConvert
{
    private static class TypeOf<T>
    {
        public static readonly ScriptType Type = ScriptTypes.Get(typeof(T));
    }

    public static bool Carries(Type clrType) => ScriptTypes.TryGet(clrType, out var type) && Carries(type);

    public static bool Carries(ScriptType type)
    {
        if (!type.IsHandle)
            return true;
        if (type == ScriptTypes.String || type.ClrType == typeof(Uri) || typeof(ScriptJson).IsAssignableFrom(type.ClrType))
            return true;
        return type.Family switch
        {
            "list" => Carries(type.Arguments[0]),
            "dict" => CarriesKey(type.Arguments[0]) && Carries(type.Arguments[1]),
            _ => false,
        };
    }

    // A dictionary key has to come back from the text JSON keeps it as.
    public static bool CarriesKey(ScriptType type) => ScriptTextForm.Has(type);

    public static bool CarriesKey(Type clrType) => ScriptTypes.TryGet(clrType, out var type) && CarriesKey(type);

    public static ScriptJson From<T>(ScriptCollectionBudget budget, T value) => From(budget, TypeOf<T>.Type, value);

    public static ScriptJson From(ScriptCollectionBudget budget, ScriptType type, object? value)
    {
        switch (type.Kind)
        {
            case ScriptTypeKind.Bool:
                return ScriptJsonValue.Of(value is true);
            case ScriptTypeKind.Int:
                return ScriptJsonValue.Of(budget, (long)(value is int i ? i : 0));
            case ScriptTypeKind.Long:
                return ScriptJsonValue.Of(budget, value is long l ? l : 0L);
            case ScriptTypeKind.Float:
                return ScriptJsonValue.Of(budget, value is float f ? f : 0f);
            case ScriptTypeKind.Double:
                return ScriptJsonValue.Of(budget, value is double d ? d : 0d);
            case ScriptTypeKind.Vector:
            {
                value ??= type.DefaultValue;
                var lanes = new ScriptJson[type.Leaves.Length];
                for (int n = 0; n < lanes.Length; n++)
                {
                    lanes[n] = type.Leaves[n].Get(value!) switch
                    {
                        float single => ScriptJsonValue.Of(budget, single),
                        double precise => ScriptJsonValue.Of(budget, precise),
                        int whole => ScriptJsonValue.Of(budget, (long)whole),
                        long wide => ScriptJsonValue.Of(budget, wide),
                        bool flag => ScriptJsonValue.Of(flag),
                        _ => ScriptJsonValue.Null,
                    };
                }
                return ScriptJsonArray.Of(budget, lanes);
            }
            case ScriptTypeKind.Coded:
                return FromCoded(budget, type, value ?? type.DefaultValue!);
        }

        switch (value)
        {
            case null:
                return ScriptJsonValue.Null;
            case string text:
                return ScriptJsonValue.Of(budget, text);
            case Uri address:
                return ScriptJsonValue.Of(budget, address.OriginalString);
            case ScriptJson json:
                return json;
        }
        if (type.Family == "list" && value is IScriptBoxedList list)
        {
            var items = new List<ScriptJson>();
            foreach (var item in list.BoxedItems())
                items.Add(From(budget, type.Arguments[0], item));
            return ScriptJsonArray.Of(budget, items);
        }
        if (type.Family == "dict" && value is IScriptBoxedDictionary dictionary)
        {
            var members = new List<KeyValuePair<string, ScriptJson>>();
            foreach (var (key, item) in dictionary.BoxedEntries())
                members.Add(new(ScriptTextForm.Format(type.Arguments[0], key), From(budget, type.Arguments[1], item)));
            return ScriptJsonObject.Of(budget, members);
        }
        throw new ScriptLimitException($"{type.Name} has no JSON form");
    }

    public static bool TryTo<T>(ScriptCollectionBudget budget, ScriptJson? json, out T? value)
    {
        if (TryTo(budget, json, TypeOf<T>.Type, out var boxed) && boxed is T typed)
        {
            value = typed;
            return true;
        }
        value = default;
        return false;
    }

    public static bool TryTo(ScriptCollectionBudget budget, ScriptJson? json, ScriptType type, out object? value)
    {
        value = null;
        if (json == null)
            return false;
        if (typeof(ScriptJson).IsAssignableFrom(type.ClrType))
        {
            if (!type.ClrType.IsInstanceOfType(json))
                return false;
            value = json;
            return true;
        }
        if (json.Kind == ScriptJsonKind.Null)
            return false;

        var plain = json as ScriptJsonValue;
        switch (type.Kind)
        {
            case ScriptTypeKind.Bool:
                if (plain?.Kind == ScriptJsonKind.Bool)
                {
                    value = plain.Bool;
                    return true;
                }
                return FromText(type, plain, out value);
            case ScriptTypeKind.Int:
                if (plain?.Kind == ScriptJsonKind.Number)
                {
                    if (!plain.TryGetLong(out long whole) || whole < int.MinValue || whole > int.MaxValue)
                        return false;
                    value = (int)whole;
                    return true;
                }
                return FromText(type, plain, out value);
            case ScriptTypeKind.Long:
                if (plain?.Kind == ScriptJsonKind.Number)
                {
                    if (!plain.TryGetLong(out long wide))
                        return false;
                    value = wide;
                    return true;
                }
                return FromText(type, plain, out value);
            case ScriptTypeKind.Float:
                if (plain?.Kind == ScriptJsonKind.Number)
                {
                    float single = (float)plain.Number;
                    if (!float.IsFinite(single))
                        return false;
                    value = single;
                    return true;
                }
                return FromText(type, plain, out value);
            case ScriptTypeKind.Double:
                if (plain?.Kind == ScriptJsonKind.Number)
                {
                    value = plain.Number;
                    return true;
                }
                return FromText(type, plain, out value);
            case ScriptTypeKind.Vector:
                return ToVector(type, json, out value);
            case ScriptTypeKind.Coded:
                return ToCoded(type, plain, out value);
        }

        if (type == ScriptTypes.String)
        {
            value = plain switch
            {
                { Kind: ScriptJsonKind.String } => plain.Text,
                { Kind: ScriptJsonKind.Number } => plain.NumberText,
                { Kind: ScriptJsonKind.Bool } => plain.Bool ? "true" : "false",
                _ => ScriptJsonWriter.Write(json, false, int.MaxValue),
            };
            return true;
        }
        if (type.ClrType == typeof(Uri))
        {
            var text = plain?.Text?.Trim();
            if (string.IsNullOrEmpty(text) || !Uri.TryCreate(text, UriKind.Absolute, out var url)
                || !text.StartsWith(url.Scheme + ":", StringComparison.OrdinalIgnoreCase))
                return false;
            value = url;
            return true;
        }
        if (type.Family == "list" && json is ScriptJsonArray array)
            return ToList(budget, type, array, out value);
        if (type.Family == "dict" && json is ScriptJsonObject obj)
            return ToDictionary(budget, type, obj, out value);
        return false;
    }

    private static ScriptJson FromCoded(ScriptCollectionBudget budget, ScriptType type, object value)
    {
        var clr = type.ClrType;
        if (clr == typeof(byte) || clr == typeof(sbyte) || clr == typeof(short) || clr == typeof(ushort) || clr == typeof(uint))
            return ScriptJsonValue.Of(budget, Convert.ToInt64(value, CultureInfo.InvariantCulture));
        if (clr == typeof(ulong))
            return ScriptJsonValue.Of(budget, (ulong)value);
        if (clr == typeof(Half))
            return ScriptJsonValue.Of(budget, (float)(Half)value);
        var document = type.Codec!.ToGraph(value);
        return document.Kind == GraphValueKind.Number
            ? ScriptJsonValue.Of(budget, document.Number)
            : ScriptJsonValue.Of(budget, document.String ?? type.Codec.Format(value));
    }

    private static bool ToCoded(ScriptType type, ScriptJsonValue? plain, out object? value)
    {
        value = null;
        if (plain == null)
            return false;
        var clr = type.ClrType;
        if (plain.Kind == ScriptJsonKind.Number)
        {
            if (clr == typeof(ulong))
            {
                if (!plain.TryGetULong(out ulong large))
                    return false;
                value = large;
                return true;
            }
            if (clr == typeof(byte) || clr == typeof(sbyte) || clr == typeof(short) || clr == typeof(ushort) || clr == typeof(uint))
            {
                if (!plain.TryGetLong(out long whole))
                    return false;
                try
                {
                    value = Convert.ChangeType(whole, clr, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (OverflowException)
                {
                    return false;
                }
            }
            if (clr == typeof(Half))
            {
                var half = (Half)plain.Number;
                if (!Half.IsFinite(half))
                    return false;
                value = half;
                return true;
            }
            return type.Codec!.TryFromGraph(GraphValue.Of(plain.Number), out value);
        }
        return plain.Kind == ScriptJsonKind.String && type.Codec!.TryParse(plain.Text!, out value);
    }

    private static bool FromText(ScriptType type, ScriptJsonValue? plain, out object? value)
    {
        value = null;
        return plain?.Kind == ScriptJsonKind.String && ScriptTextForm.TryParse(type, plain.Text, out value);
    }

    // [x,y,z] in lane order, {"x":..,"y":..} by field name for a struct of plain fields, or its text form.
    private static bool ToVector(ScriptType type, ScriptJson json, out object? value)
    {
        value = null;
        var leaves = type.Leaves;
        var boxed = Activator.CreateInstance(type.ClrType)!;
        switch (json)
        {
            case ScriptJsonArray array:
                if (array.Count != leaves.Length)
                    return false;
                for (int n = 0; n < leaves.Length; n++)
                {
                    if (!TryLane(leaves[n].ClrType, array.Items[n], out var lane))
                        return false;
                    leaves[n].Set(boxed, lane);
                }
                value = boxed;
                return true;
            case ScriptJsonObject obj:
                if (obj.Count != leaves.Length)
                    return false;
                for (int n = 0; n < leaves.Length; n++)
                {
                    if (leaves[n].Path.Length != 1 || !TryField(obj, leaves[n].Path[0].Name, out var member)
                        || !TryLane(leaves[n].ClrType, member, out var lane))
                        return false;
                    leaves[n].Set(boxed, lane);
                }
                value = boxed;
                return true;
            default:
                return FromText(type, json as ScriptJsonValue, out value);
        }
    }

    // Exact first, then ignoring case, so "x" finds x and "X" does too.
    private static bool TryField(ScriptJsonObject obj, string name, out ScriptJson member)
    {
        if (obj.TryGet(name, out member))
            return true;
        foreach (var (key, found) in obj.Members)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                member = found;
                return true;
            }
        }
        return false;
    }

    private static bool TryLane(Type clr, ScriptJson json, out object lane)
    {
        lane = 0;
        if (json is not ScriptJsonValue plain)
            return false;
        if (clr == typeof(bool))
        {
            if (plain.Kind != ScriptJsonKind.Bool)
                return false;
            lane = plain.Bool;
            return true;
        }
        if (plain.Kind != ScriptJsonKind.Number)
            return false;
        if (clr == typeof(float))
        {
            float single = (float)plain.Number;
            lane = single;
            return float.IsFinite(single);
        }
        if (clr == typeof(double))
        {
            lane = plain.Number;
            return true;
        }
        if (!plain.TryGetLong(out long whole))
            return false;
        if (clr == typeof(long))
        {
            lane = whole;
            return true;
        }
        if (clr == typeof(int) && whole >= int.MinValue && whole <= int.MaxValue)
        {
            lane = (int)whole;
            return true;
        }
        return false;
    }

    // An item that does not read is the type's default in its place, and the whole is then not read.
    private static bool ToList(ScriptCollectionBudget budget, ScriptType type, ScriptJsonArray array, out object? value)
    {
        var itemType = type.Arguments[0];
        var list = (IScriptBoxedList)Activator.CreateInstance(type.ClrType, budget)!;
        bool all = true;
        foreach (var item in array.Items)
        {
            all &= TryTo(budget, item, itemType, out var converted);
            list.AddBoxed(converted ?? itemType.DefaultValue);
        }
        value = list;
        return all;
    }

    // A key that does not read as the key type is left out, and a value that does not read is the type's
    // default; either way the whole is then not read.
    private static bool ToDictionary(ScriptCollectionBudget budget, ScriptType type, ScriptJsonObject obj, out object? value)
    {
        var keyType = type.Arguments[0];
        var valueType = type.Arguments[1];
        var dictionary = (IScriptBoxedDictionary)Activator.CreateInstance(type.ClrType, budget)!;
        bool all = true;
        foreach (var (text, item) in obj.Members)
        {
            if (!ScriptTextForm.TryParse(keyType, text, out var key) || key == null)
            {
                all = false;
                continue;
            }
            all &= TryTo(budget, item, valueType, out var converted);
            dictionary.SetBoxed(key, converted ?? valueType.DefaultValue);
        }
        value = dictionary;
        return all;
    }
}
