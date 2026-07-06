// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Threading;
using System.Threading.Tasks;
using Lumora.Core.Input;

namespace Lumora.Core.Components.Avatar;

// Handler that fills an empty AvatarSocket with a default piece. Set on
// AvatarEquipManager.SocketFiller. When AvatarEquipManager.FillEmptySockets runs, it
// invokes this for every body-node slot that has nothing equipped, giving
// the user a default head / hands / view when no custom avatar is worn.
// Extends IWorldElement so it can be a SyncRef target. - xlinka
public interface IAvatarSocketFiller : IWorldElement
{
    Task FillSocket(BodyNode node, AvatarEquipManager manager, CancellationToken cancellationToken);
}
