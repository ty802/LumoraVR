// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Shading;

// A uniform the compiled shader declares: a property a material can change without a recompile. A
// texture uniform from a slot that holds a texture carries that slot's binding, so the material can hand
// the shader the texture the graph was given. Binding is -1 otherwise.
public sealed record ShaderUniform(string Name, ShaderType Type, string Hints, string? Default, int Binding, string? NodeId)
{
    public string Declaration
    {
        get
        {
            var text = $"uniform {ShaderTypes.GlslName(Type)} {Name}";
            if (Hints.Length > 0)
                text += " : " + Hints;
            if (Default != null)
                text += " = " + Default;
            return text + ";";
        }
    }
}
