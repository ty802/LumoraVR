// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// A message from another script of the same author: its text, its number, and who sent it when it came
// from a Local script on someone's machine.
public readonly record struct MessageReceived(string Message, float Value, User? From);
