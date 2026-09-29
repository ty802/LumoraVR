// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// Who is subscribed now (the one receiving it, for a gift), the message they shared, their months in
// total as the chat counts them, the plan as the chat names it ("Prime", "1000", "2000", "3000"), and for a
// gift who gave it, empty when they gave it anonymously.
public readonly record struct StreamSubscription(string Name, string Login, string Message, int Months, string Plan,
    bool Resub, bool Gifted, string GiftedBy, bool Anonymous);
