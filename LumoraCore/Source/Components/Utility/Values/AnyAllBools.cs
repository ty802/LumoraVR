// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Utility;

// How the inputs of an AnyAllBools combine.
public enum BoolCombineMode
{
    // True when every input is true. An empty list is true.
    All,
    // True when at least one input is true. An empty list is false.
    Any,
}

public sealed class BoolInput : SyncElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly SyncRef<IField<bool>> Field = new();
    public readonly Sync<bool> Invert = new();

    public override void Initialize(World world, IWorldElement? parent)
    {
        base.Initialize(world, parent);
        SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalClearDirty() { }

    // A cleared reference reads false.
    public bool Evaluate()
    {
        var field = Field.Target;
        bool value = field != null && field.Value;
        return Invert.Value ? !value : value;
    }

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("Field", Field.Save(control));
        dictionary.Add("Invert", Invert.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("Field") is { } fieldNode)
            Field.Load(fieldNode, control);
        if (dictionary.TryGetNode("Invert") is { } invertNode)
            Invert.Load(invertNode, control);
    }

    public override object? GetValueAsObject() => Evaluate();
}

[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
public class AnyAllBools : Component
{
    public readonly Sync<BoolCombineMode> Mode;

    public readonly Sync<bool> Invert;

    public readonly SyncList<BoolInput> Inputs;

    public readonly FieldDrive<bool> Target;

    public AnyAllBools()
    {
        Mode = new Sync<BoolCombineMode>(this, BoolCombineMode.All);
        Invert = new Sync<bool>(this, false);
        Inputs = new SyncList<BoolInput>();
        Target = new FieldDrive<bool>(this) { LocalValueOnly = true };
    }

    public BoolInput AddInput(IField<bool>? field, bool invert = false)
    {
        var input = Inputs.Add();
        input.Field.Target = field!;
        input.Invert.Value = invert;
        return input;
    }

    public bool Result
    {
        get
        {
            bool all = Mode.Value == BoolCombineMode.All;
            // Empty list: all-of is vacuously true, any-of has nothing to find. Matches how the two read
            // in a condition, and stops a half-built setup from flickering the target.
            bool result = all;
            foreach (var input in Inputs.Elements)
            {
                bool value = input.Evaluate();
                if (all)
                {
                    if (!value)
                    {
                        result = false;
                        break;
                    }
                }
                else if (value)
                {
                    result = true;
                    break;
                }
            }
            return Invert.Value ? !result : result;
        }
    }

    public override void OnUpdate(float delta)
    {
        Target.SetValue(Result);
    }
}
