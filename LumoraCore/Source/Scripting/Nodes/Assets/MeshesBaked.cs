// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// What Bake Meshes came back with: the slot holding the merged mesh, how many meshes went into it, how many
// were left as they were, and why nothing was baked when nothing was.
public readonly record struct MeshesBaked(Slot? Baked, int Meshes, int Left, string Error);
