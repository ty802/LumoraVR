// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;

namespace Lumora.Core.Templates;

public static class WorldTemplates
{
    private const string EmptyTemplateName = "Empty";
    private const string LocalHomeTemplateName = "LocalHome";
    private const string GridTemplateName = "Grid";
    private const string ShaderTestTemplateName = "ShaderTest";

    private static readonly IReadOnlyDictionary<string, WorldTemplateDefinition> s_templates =
        new Dictionary<string, WorldTemplateDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            { EmptyTemplateName, new EmptyWorldTemplate() },
            { LocalHomeTemplateName, new LocalHomeWorldTemplate() },
            { GridTemplateName, new GridSpaceWorldTemplate() },
            { ShaderTestTemplateName, new ShaderTestWorldTemplate() }
        };

    /// <summary>Template names available to pick when creating a world, in display order.</summary>
    public static IReadOnlyList<string> AvailableTemplates { get; } = new[]
    {
        LocalHomeTemplateName,
        GridTemplateName,
        ShaderTestTemplateName,
        EmptyTemplateName,
    };

    public static Action<World> GetTemplate(string name)
    {
        return GetDefinition(name).Apply;
    }

    public static void ApplyTemplate(World world, string templateName)
    {
        GetTemplate(templateName)?.Invoke(world);
    }

    /// <summary>Modes the given template may be hosted in (e.g. to constrain a host-time picker).</summary>
    public static IReadOnlyList<WorldMode> AllowedModes(string templateName) => GetDefinition(templateName).AllowedModes;

    /// <summary>Default mode for the given template.</summary>
    public static WorldMode DefaultMode(string templateName) => GetDefinition(templateName).DefaultMode;

    private static WorldTemplateDefinition GetDefinition(string name)
    {
        name ??= string.Empty;

        if (s_templates.TryGetValue(name, out var template))
        {
            return template;
        }

        return s_templates[EmptyTemplateName];
    }
}
