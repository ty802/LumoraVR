// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lumora.Core.Assets.Interop.Unity;

// A package avatar's expressions menu: up to eight controls per page, each a button or toggle that sets a
// parameter to a value, a submenu, or a puppet (a radial or stick that sets parameters continuously).
// The parameters asset beside it says what each parameter holds and what it starts at. -xlinka
public sealed class UnityExpressionMenu
{
    public enum ControlType { Button = 101, Toggle = 102, SubMenu = 103, TwoAxis = 201, FourAxis = 202, Radial = 203 }

    public sealed class Control
    {
        public string Name = "";
        public ControlType Type;
        public string Parameter = "";
        public float Value;
        public string? SubMenu;
        public string? Icon;
        public readonly List<string> SubParameters = new();
    }

    public readonly List<Control> Controls = new();

    public static UnityExpressionMenu? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        var doc = UnityYaml.Parse(text).FirstOrDefault(d => d["controls"] is UnitySeq);
        if (doc?["controls"] is not UnitySeq controls)
            return null;
        var menu = new UnityExpressionMenu();
        foreach (var c in controls.Items)
        {
            var control = new Control
            {
                Name = CleanName(c["name"]?.Text),
                Type = (ControlType)(c["type"]?.Int() ?? 102),
                Parameter = c["parameter"]?["name"]?.Text ?? "",
                Value = c["value"]?.Float(1f) ?? 1f,
                SubMenu = c["subMenu"]?.Ref.Guid,
                Icon = c["icon"]?.Ref.Guid,
            };
            if (c["subParameters"] is UnitySeq sub)
                foreach (var p in sub.Items)
                    control.SubParameters.Add(p["name"]?.Text ?? "");
            menu.Controls.Add(control);
        }
        return menu;
    }

    // Menu names carry rich text tags for colour and size, and a slash would read as a folder here.
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);

    public static string CleanName(string? name)
    {
        var text = Tags.Replace(name ?? "", "").Replace('/', '-').Replace('\n', ' ').Trim();
        return text.Length > 0 ? text : "Unnamed";
    }
}

public sealed class UnityExpressionParameters
{
    public enum ValueType { Int = 0, Float = 1, Bool = 2 }

    public sealed record Parameter(string Name, ValueType Type, float Default, bool Saved, bool Synced = true);

    public readonly Dictionary<string, Parameter> ByName = new(StringComparer.Ordinal);

    public static UnityExpressionParameters? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        var doc = UnityYaml.Parse(text).FirstOrDefault(d => d["parameters"] is UnitySeq);
        if (doc?["parameters"] is not UnitySeq parameters)
            return null;
        var result = new UnityExpressionParameters();
        foreach (var p in parameters.Items)
        {
            var name = p["name"]?.Text;
            if (string.IsNullOrEmpty(name))
                continue;
            result.ByName[name] = new Parameter(name, (ValueType)(p["valueType"]?.Int() ?? 0), p["defaultValue"]?.Float() ?? 0f,
                (p["saved"]?.Int(1) ?? 1) != 0, (p["networkSynced"]?.Int(1) ?? 1) != 0);
        }
        return result;
    }
}
