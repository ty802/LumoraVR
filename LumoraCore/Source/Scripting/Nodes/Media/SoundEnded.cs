// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// How a sound played once ended: nothing in error when it played through, else why it did not play.
public readonly record struct SoundEnded(string Error);
