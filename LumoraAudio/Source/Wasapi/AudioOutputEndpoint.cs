// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Audio.Wasapi;

// One output device as something to play into. Id is what WasapiEndpointOutput opens, Name is what a
// person picks it by, IsDefault marks the one Windows plays everything else on.
public readonly record struct AudioOutputEndpoint(string Id, string Name, bool IsDefault);
