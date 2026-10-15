// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;

namespace Lumora.Core.Templates;

public static class WorldTemplates
{
    private const string LocalHomeTemplateName = "LocalHome";
    private const string GridTemplateName = "Grid";
    private const string ScratchTemplateName = "Scratch";
    private const string ShadersTemplateName = "Shaders";

    private static readonly IReadOnlyDictionary<string, WorldTemplateDefinition> s_templates =
        new Dictionary<string, WorldTemplateDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            { LocalHomeTemplateName, new LocalHomeWorldTemplate() },
            { GridTemplateName, new GridSpaceWorldTemplate() },
            { ScratchTemplateName, new ScratchSpaceWorldTemplate() },
            { ShadersTemplateName, new ShaderGalleryWorldTemplate() }
        };

    // A blank/unknown name builds nothing. This is the base a saved world or a fresh session populates itself,
    // so a template's default content isn't layered underneath it. Not a pickable world type. -xlinka
    private static readonly WorldTemplateDefinition s_blank = new BlankWorld();

    // In display order.
    public static IReadOnlyList<string> AvailableTemplates { get; } = new[]
    {
        LocalHomeTemplateName,
        GridTemplateName,
        ScratchTemplateName,
        ShadersTemplateName,
    };

    public static Action<World> GetTemplate(string name)
    {
        return GetDefinition(name).Apply;
    }

    public static void ApplyTemplate(World world, string templateName)
    {
        GetTemplate(templateName)?.Invoke(world);
    }

    // The template built into a world held before it runs, a few steps a frame; whenDone runs after the last.
    public static void ApplyTemplateStaged(World world, string templateName, Action? whenDone = null)
    {
        GetDefinition(templateName).ApplyStaged(world, whenDone);
    }

    public static IReadOnlyList<WorldMode> AllowedModes(string templateName) => GetDefinition(templateName).AllowedModes;

    public static WorldMode DefaultMode(string templateName) => GetDefinition(templateName).DefaultMode;

    private static WorldTemplateDefinition GetDefinition(string name)
    {
        if (name != null && s_templates.TryGetValue(name, out var template))
            return template;
        return s_blank;
    }

    private sealed class BlankWorld : WorldTemplateDefinition
    {
        public BlankWorld() : base("Blank") { }
        protected override void Build(World world) { }
        protected override void PostBuild(World world) { }
    }
}
