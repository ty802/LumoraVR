// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Import;

// Only includes materials Lumora actually has today. As more material providers
// land extend this enum and add the corresponding presets in ModelImportDialog. - xlinka
// Lit is first so it's the default: imported models/avatars are shaded by scene lighting
// (PBS metallic). Unlit is opt-in for flat/self-lit content.
public enum ModelMaterialType
{
    Lit,
    Unlit,
}
