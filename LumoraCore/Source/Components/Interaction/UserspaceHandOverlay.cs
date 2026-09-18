// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Components.UI;
using Lumora.Core.Input;

namespace Lumora.Core.Components.Interaction;

// Lifts one hand's world visuals into the band above the dash while a userspace panel is up, and puts
// them back exactly when it closes. The visuals are whatever is equipped on that hand's socket: the
// default skeleton hand, or a hand piece of an avatar built from parts. A full-body avatar drives its
// hands through one skinned body that cannot be split, so it keeps them where the body is. Reconciled
// every frame because the piece can be swapped or destroyed while the dash is open, and a renderer that
// left the set is restored rather than left lifted. Depth test stays on: the dash writes no depth, so
// order alone puts the hand over it, and the hand keeps occluding itself. -xlinka
public sealed class UserspaceHandOverlay
{
    private readonly List<MeshRenderer> _lifted = new();
    private readonly List<MeshRenderer> _wanted = new();
    private AvatarSocket? _socket;
    private UserRoot? _socketRoot;

    public IReadOnlyList<MeshRenderer> Lifted => _lifted;

    public void Sync(UserRoot? root, Chirality side, bool lift)
    {
        _wanted.Clear();
        if (lift && root != null && !root.IsDestroyed)
            CollectHandRenderers(root, side, _wanted);

        for (int i = _lifted.Count - 1; i >= 0; i--)
        {
            var renderer = _lifted[i];
            if (!renderer.IsDestroyed && _wanted.Contains(renderer))
                continue;
            if (!renderer.IsDestroyed)
                renderer.ClearOverlayOrder();
            _lifted.RemoveAt(i);
        }

        for (int i = 0; i < _wanted.Count; i++)
        {
            var renderer = _wanted[i];
            if (renderer.IsDestroyed)
                continue;
            if (!_lifted.Contains(renderer))
                _lifted.Add(renderer);
            renderer.SetOverlayOrder(UserspaceRenderBands.Hands, depthTest: true);
        }
        _wanted.Clear();
    }

    public void RestoreAll()
    {
        Sync(null, Chirality.None, false);
    }

    private void CollectHandRenderers(UserRoot root, Chirality side, List<MeshRenderer> into)
    {
        var node = side == Chirality.Left ? BodyNode.LeftHand : BodyNode.RightHand;
        if (_socket == null || _socket.IsDestroyed || !ReferenceEquals(_socketRoot, root) || _socket.Node.Value != node)
        {
            _socket = root.GetRegisteredComponent<AvatarSocket>(s => s.Node.Value == node);
            _socketRoot = root;
        }

        var piece = (_socket?.Equipped?.Target as Component)?.Slot;
        if (piece == null || piece.IsDestroyed)
            return;
        piece.GetComponentsInChildren(into, includeSelf: true);
    }
}
