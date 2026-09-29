// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Scripting;

// One chat message: its text, who sent it (shown name, login, the chat's own id for them, their name
// colour as the chat gives it, sRGB), what they are in the channel, whether it is a /me action or
// highlighted, the bits cheered with it, the reward it redeemed (empty for none) and its id.
public readonly record struct StreamChatMessage(string Text, string Name, string Login, string UserId, color Color,
    bool Moderator, bool Subscriber, bool Broadcaster, bool Vip, bool FirstMessage, bool Action, bool Highlighted, int Bits,
    string RewardId, string MessageId);
