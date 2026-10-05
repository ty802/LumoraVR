// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks;

// The fur material's shell passes. A Godot material draws once, so the twenty shells are twenty materials
// chained by next_pass behind the skin, all wearing the one shell shader and told apart only by their
// fur_layer, 0.05 apart up to 1. Each is held by the pass before it, so dropping the skin's chain frees
// them all. Every property the fur material sets goes to every link; the layer is set here once and
// nothing else writes it. -xlinka
internal static class FurShells
{
    public const int Count = 20;
    private const string ShellShaderPath = "res://Shaders/Mat_FurShell.gdshader";
    private static readonly StringName FurLayer = new("fur_layer");

    // Chains the shells behind the skin and returns the first one.
    public static ShaderMaterial Chain(ShaderMaterial skin)
    {
        var shader = ResourceLoader.Exists(ShellShaderPath) ? GD.Load<Shader>(ShellShaderPath) : null;
        if (shader == null)
            LumoraLogger.Warn($"FurShells: shell shader not found at {ShellShaderPath}");

        ShaderMaterial first = null!;
        ShaderMaterial previous = skin;
        for (int i = 1; i <= Count; i++)
        {
            var shell = new ShaderMaterial { Shader = shader };
            shell.SetShaderParameter(FurLayer, i / (float)Count);
            previous.NextPass = shell;
            first ??= shell;
            previous = shell;
        }
        return first;
    }
}
