// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Scripting;

// The channel that raided: its shown name, login, the chat's id for it, its name colour, and how many
// viewers it brought.
public readonly record struct StreamRaid(string Name, string Login, string UserId, color Color, int Viewers);
