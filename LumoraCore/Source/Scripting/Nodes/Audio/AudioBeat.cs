// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// How far the onset cleared the detector's threshold in dB, and the tempo the analyser was hearing then (0
// when it has none yet).
public readonly record struct AudioBeat(float Strength, float Bpm);
