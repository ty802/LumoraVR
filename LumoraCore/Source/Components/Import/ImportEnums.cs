// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Import;

// Only includes materials Lumora actually has today. As more material providers
// land extend this enum and add the corresponding presets in ModelImportDialog. - xlinka
public enum ModelMaterialType
{
    Unlit,
    CustomShader,
}
