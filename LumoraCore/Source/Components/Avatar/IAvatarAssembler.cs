// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Avatar;

// Builds the per-user scene setup onto a UserRoot. Split into two halves so equipment is owned per-peer:
// the authority builds the shared scaffold (body-node tracking, collider, locomotion, head output, avatar
// manager) for everyone, and each peer builds its OWN equipment (hand tool rig, context menu) in its own
// RefID byte. BuildAvatar does both - used for the host's own avatar where it owns everything. - xlinka
public interface IAvatarAssembler
{
    // Host-built, replicated to all: body nodes, collider, locomotion, head output, avatar manager.
    void BuildSharedScaffold(UserRoot user);
    // Built by the OWNING peer in its own byte: hand tool rig + context menu. Idempotent.
    void BuildOwnedEquipment(UserRoot user);
    // Convenience that does both, for the host's own avatar.
    void BuildAvatar(UserRoot user);
}
