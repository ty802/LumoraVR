// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Audio;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Audio;

// The shape of one voice mode: how loud, how far, and how it is positioned. A nested sync object so a
// component can carry four of them as ordinary members and an avatar author can retune any field
// without a component per mode.
//
// Nothing here is read by the mixer directly. AvatarAudioOutputManager drives an AudioOutput's fields
// from whichever config the speaker's current mode selects, so the emitter stays the single place the
// mixer looks at and these stay authored data. -xlinka
public sealed class VoiceModeConfig : SyncElement
{
    // Whisper's whole point is that it stops at arm's length. The manager needs the same two numbers to
    // decide whether a remote whisperer is audible at all, so they are named here rather than typed out
    // again next to the check.
    public const float WhisperMinDistance = 1.125f;
    public const float WhisperMaxDistance = 1.25f;

    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly Sync<float> Volume = new(1f);
    public readonly Sync<float> MinDistance = new(1f);
    public readonly Sync<float> MaxDistance = new(500f);
    public readonly Sync<AudioRolloffCurve> RollOffMode = new(AudioRolloffCurve.LogarithmicFadeOff);
    public readonly Sync<AudioDistanceSpace> DistanceSpace = new(AudioDistanceSpace.Local);
    public readonly Sync<float> MinScale = new(1f);
    public readonly Sync<float> MaxScale = new(float.PositiveInfinity);
    public readonly Sync<bool> Spatialize = new(true);
    public readonly Sync<float> SpatialBlend = new(1f);
    public readonly Sync<float> Doppler = new(0f);
    public readonly Sync<bool> IgnoreAudioEffects = new(false);

    // Whether a listener is allowed to turn the effect bypass on for themselves. Whisper sets it because
    // a whisper is meant to arrive dry wherever you are standing; a shout across a cathedral is not.
    public readonly Sync<bool> AllowLocallyIgnoringAudioEffects = new(false);

    public override void Initialize(World world, IWorldElement? parent)
    {
        base.Initialize(world, parent);
        SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
    }

    public void ApplyPreset(VoiceMode mode)
    {
        switch (mode)
        {
            case VoiceMode.Whisper:
                Set(0.75f, WhisperMinDistance, WhisperMaxDistance, AudioRolloffCurve.Linear,
                    AudioDistanceSpace.Global, spatialize: true, blend: 1f, ignoreEffects: true,
                    allowLocalIgnore: true);
                break;
            case VoiceMode.Shout:
                Set(1f, 1f, 100f, AudioRolloffCurve.Linear, AudioDistanceSpace.Local,
                    spatialize: true, blend: 1f, ignoreEffects: false, allowLocalIgnore: false);
                break;
            case VoiceMode.Broadcast:
                // Heard everywhere at full volume and from nowhere in particular: the distances are past
                // any room and the positioning is off, so a broadcast does not pan as the speaker walks.
                Set(1f, 100000f, 100000f, AudioRolloffCurve.Linear, AudioDistanceSpace.Global,
                    spatialize: false, blend: 0f, ignoreEffects: false, allowLocalIgnore: false);
                break;
            default:
                Set(1f, 1f, 500f, AudioRolloffCurve.LogarithmicFadeOff, AudioDistanceSpace.Local,
                    spatialize: true, blend: 1f, ignoreEffects: false, allowLocalIgnore: false);
                break;
        }
    }

    private void Set(float volume, float minDistance, float maxDistance, AudioRolloffCurve rolloff,
        AudioDistanceSpace space, bool spatialize, float blend, bool ignoreEffects, bool allowLocalIgnore)
    {
        Volume.Value = volume;
        MinDistance.Value = minDistance;
        MaxDistance.Value = maxDistance;
        RollOffMode.Value = rolloff;
        DistanceSpace.Value = space;
        MinScale.Value = 1f;
        MaxScale.Value = float.PositiveInfinity;
        Spatialize.Value = spatialize;
        SpatialBlend.Value = blend;
        Doppler.Value = 0f;
        IgnoreAudioEffects.Value = ignoreEffects;
        AllowLocallyIgnoringAudioEffects.Value = allowLocalIgnore;
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalClearDirty() { }

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("Volume", Volume.Save(control));
        dictionary.Add("MinDistance", MinDistance.Save(control));
        dictionary.Add("MaxDistance", MaxDistance.Save(control));
        dictionary.Add("RollOffMode", RollOffMode.Save(control));
        dictionary.Add("DistanceSpace", DistanceSpace.Save(control));
        dictionary.Add("MinScale", MinScale.Save(control));
        dictionary.Add("MaxScale", MaxScale.Save(control));
        dictionary.Add("Spatialize", Spatialize.Save(control));
        dictionary.Add("SpatialBlend", SpatialBlend.Save(control));
        dictionary.Add("Doppler", Doppler.Save(control));
        dictionary.Add("IgnoreAudioEffects", IgnoreAudioEffects.Save(control));
        dictionary.Add("AllowLocallyIgnoringAudioEffects", AllowLocallyIgnoringAudioEffects.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        LoadMember(dictionary, "Volume", Volume, control);
        LoadMember(dictionary, "MinDistance", MinDistance, control);
        LoadMember(dictionary, "MaxDistance", MaxDistance, control);
        LoadMember(dictionary, "RollOffMode", RollOffMode, control);
        LoadMember(dictionary, "DistanceSpace", DistanceSpace, control);
        LoadMember(dictionary, "MinScale", MinScale, control);
        LoadMember(dictionary, "MaxScale", MaxScale, control);
        LoadMember(dictionary, "Spatialize", Spatialize, control);
        LoadMember(dictionary, "SpatialBlend", SpatialBlend, control);
        LoadMember(dictionary, "Doppler", Doppler, control);
        LoadMember(dictionary, "IgnoreAudioEffects", IgnoreAudioEffects, control);
        LoadMember(dictionary, "AllowLocallyIgnoringAudioEffects", AllowLocallyIgnoringAudioEffects, control);
    }

    public override object? GetValueAsObject() => Volume.Value;

    private static void LoadMember(DataTreeDictionary dictionary, string name, SyncElement member, LoadControl control)
    {
        if (dictionary.TryGetNode(name) is { } child)
            member.Load(child, control);
    }
}
