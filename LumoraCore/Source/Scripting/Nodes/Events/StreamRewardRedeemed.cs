// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// The reward's id, who redeemed it, and the text they gave with it.
public readonly record struct StreamRewardRedeemed(string RewardId, string Name, string Login, string UserId, string Text);
