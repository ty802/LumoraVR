// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;

namespace Lumora.Godot.Hooks;

// Intermediate generic base for hooks whose only job is "manage one platform
// node hanging off the slot's Node3D" (lights, cameras, etc.). Sits between
// the generic ComponentHook and concrete simple hooks so the boilerplate
// (lifecycle, parenting, teardown) lives in one place. Subclasses declare the
// platform node type via N and only have to:
//   1) build the node in CreatePlatformNode()
//   2) push owner state into it in SyncProperties()
// - xlinka
public abstract class NodeBackedComponentHook<D, N> : ComponentHook<D>
    where D : ImplementableComponent<IHook>
    where N : Node
{
    private N _node = null!;

    public N PlatformNode => _node;

    protected abstract N CreatePlatformNode();

    protected abstract void SyncProperties();

    protected virtual string NodeName => GetType().Name.Replace("Hook", "");

    public override void Initialize()
    {
        base.Initialize();

        _node = CreatePlatformNode();
        if (_node != null)
        {
            if (string.IsNullOrEmpty(_node.Name))
                _node.Name = NodeName;
            attachedNode.AddChild(_node);
        }

        OnAfterAttach();
    }

    public override void ApplyChanges()
    {
        if (_node == null || !GodotObject.IsInstanceValid(_node))
            return;
        SyncProperties();
    }

    // Hook for subclasses that need to do post-attach setup (signals, child
    // node wiring, etc) without rewriting Initialize.
    protected virtual void OnAfterAttach() { }

    // Replace the current platform node with a new one. Used by hooks whose
    // node *type* depends on owner state (e.g. light type switch between
    // DirectionalLight3D / OmniLight3D / SpotLight3D).
    protected void ReplacePlatformNode(N newNode)
    {
        if (_node != null && GodotObject.IsInstanceValid(_node))
            _node.QueueFree();

        _node = newNode;
        if (_node != null)
        {
            if (string.IsNullOrEmpty(_node.Name))
                _node.Name = NodeName;
            attachedNode.AddChild(_node);
        }
    }

    public override void Destroy(bool destroyingWorld)
    {
        if (!destroyingWorld && _node != null && GodotObject.IsInstanceValid(_node))
            _node.QueueFree();
        _node = null!;
        base.Destroy(destroyingWorld);
    }
}
