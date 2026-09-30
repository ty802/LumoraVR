// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Shading;

// The shader node registry: Loom's registry over this assembly only, so shader nodes never show up in a
// script's browser and a shader Add never clashes with Loom's Add. LumoraCore registers its "texture"
// handle before it first asks for this; with no LumoraCore about (a graph compiled on its own), the
// stand-in becomes the texture type itself. -xlinka
public static class ShaderNodes
{
    private static readonly object _lock = new();
    private static NodeRegistry? _registry;

    public static NodeRegistry Registry
    {
        get
        {
            var registry = _registry;
            if (registry != null)
                return registry;
            lock (_lock)
            {
                if (_registry == null)
                {
                    RegisterTypes();
                    _registry = NodeRegistry.Scan(new[] { typeof(ShaderNode).Assembly });
                }
                return _registry;
            }
        }
    }

    public static void RegisterTypes()
    {
        ScriptTypes.RegisterEnum("shaderSurface", typeof(ShaderSurface));
        ScriptTypes.RegisterEnum("shaderLighting", typeof(ShaderLighting));
        ScriptTypes.RegisterEnum("shaderCull", typeof(ShaderCull));
        ScriptTypes.RegisterEnum("shaderSpace", typeof(ShaderSpace));
        ScriptTypes.RegisterEnum("shaderUV", typeof(ShaderUV));
        ScriptTypes.RegisterEnum("shaderColorSpace", typeof(TextureColorSpace));
        ScriptTypes.RegisterEnum("shaderFilter", typeof(TextureFilter));
        ScriptTypes.RegisterEnum("shaderWrap", typeof(TextureWrap));
        ScriptTypes.RegisterEnum("shaderMix", typeof(MixMode));
        ScriptTypes.RegisterEnum("shaderCompare", typeof(CompareMode));
        if (ScriptTypes.TryGet("texture", out _))
            ScriptTypes.RegisterStandIn(typeof(ShaderTexture), "texture");
        else
            ScriptTypes.RegisterHandle("texture", typeof(ShaderTexture));
    }
}
