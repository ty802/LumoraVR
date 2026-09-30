// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Lumora.Audio;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Audio;

// One band of an EqualizerEffect. Its fields replicate and save one by one like any others; a change to any of
// them tells the equalizer that holds it, which is what gets the new curve to the mixer. A band has no single
// Value to copy, so it copies its own fields when a slot is duplicated.
public sealed class EqualizerBand : SyncElement, ISyncMemberCopy
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly Sync<BiquadFilterType> Type = new(BiquadFilterType.Peaking);

    [Range(BiquadSettings.MinFrequency, BiquadSettings.MaxFrequency, "0", exponential: true)]
    public readonly Sync<float> Frequency = new(BiquadSettings.DefaultFrequency);

    // Peaking and shelves only.
    [Range(BiquadSettings.MinGain, BiquadSettings.MaxGain, "0.0")]
    public readonly Sync<float> Gain = new(BiquadSettings.DefaultGain);

    [Range(BiquadSettings.MinQ, BiquadSettings.MaxQ, "0.00", exponential: true)]
    public readonly Sync<float> Q = new(BiquadSettings.DefaultQ);

    public readonly Sync<bool> Enabled = new(true);

    internal EqualizerEffect? Owner;

    public EqualizerBandSettings Settings => new()
    {
        Type = Type.Value,
        Frequency = Frequency.Value,
        Gain = Gain.Value,
        Q = Q.Value,
        Enabled = Enabled.Value,
    };

    public override void Initialize(World world, IWorldElement? parent)
    {
        base.Initialize(world, parent);
        SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
        Type.Changed += OnFieldChanged;
        Frequency.Changed += OnFieldChanged;
        Gain.Changed += OnFieldChanged;
        Q.Changed += OnFieldChanged;
        Enabled.Changed += OnFieldChanged;
    }

    private void OnFieldChanged(IChangeable member) => Owner?.BandChanged();

    public void CopyFromSource(ISyncMember source, Action<ISyncMember, ISyncMember> copyChild)
    {
        if (source is not EqualizerBand other || ReferenceEquals(other, this))
            return;
        copyChild(other.Type, Type);
        copyChild(other.Frequency, Frequency);
        copyChild(other.Gain, Gain);
        copyChild(other.Q, Q);
        copyChild(other.Enabled, Enabled);
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalClearDirty() { }

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("Type", Type.Save(control));
        dictionary.Add("Frequency", Frequency.Save(control));
        dictionary.Add("Gain", Gain.Save(control));
        dictionary.Add("Q", Q.Save(control));
        dictionary.Add("Enabled", Enabled.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("Type") is { } typeNode)
            Type.Load(typeNode, control);
        if (dictionary.TryGetNode("Frequency") is { } frequencyNode)
            Frequency.Load(frequencyNode, control);
        if (dictionary.TryGetNode("Gain") is { } gainNode)
            Gain.Load(gainNode, control);
        if (dictionary.TryGetNode("Q") is { } qNode)
            Q.Load(qNode, control);
        if (dictionary.TryGetNode("Enabled") is { } enabledNode)
            Enabled.Load(enabledNode, control);
    }

    public override object? GetValueAsObject() => Frequency.Value;
}
