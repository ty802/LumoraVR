// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// What one author's scripts hold between them right now: working memory, slots standing,
// slots made in the last minute, and rays cast this frame.
public readonly record struct ScriptAuthorAccount(string AuthorKey, long Memory, int Standing, int MadeLastMinute, int CastsThisFrame);
