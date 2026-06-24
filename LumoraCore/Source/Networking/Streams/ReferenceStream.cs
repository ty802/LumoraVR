// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Networking.Streams;

/// <summary>
/// Stream that syncs a reference to a world element by RefID. The owner writes the target; receivers
/// resolve the RefID to the live object, retrying each frame until it exists (a high-frequency stream
/// can arrive before the element it points at does). -xlinka
/// </summary>
public class ReferenceStream<T> : ImplicitStream where T : class, IWorldElement
{
    private RefID _targetRefID = RefID.Null;
    private T? _target;
    private bool _receivedFirstData;
    private bool _updateTarget;

    /// <summary>
    /// The referenced element. Set on the owner; resolved from the synced RefID on receivers.
    /// </summary>
    public T? Target
    {
        get => _target;
        set
        {
            CheckOwnership();
            _target = value;
            _targetRefID = value?.ReferenceID ?? RefID.Null;
        }
    }

    public override bool HasValidData => _receivedFirstData || IsLocal;

    public override void Update()
    {
        base.Update();

        // Only receivers resolve; the owner already holds the live target.
        if (IsLocal || !_updateTarget)
            return;

        if (_targetRefID == RefID.Null)
        {
            _target = null;
            _updateTarget = false;
            return;
        }

        _target = World.ReferenceController.GetObjectOrNull(in _targetRefID) as T;
        if (_target != null)
            _updateTarget = false; // resolved - stop retrying
    }

    public override void Encode(BinaryWriter writer)
    {
        if (_targetRefID == RefID.Null)
        {
            writer.Write(false);
            return;
        }

        writer.Write(true);
        writer.Write(_targetRefID.RawValue);
    }

    public override void Decode(BinaryReader reader, StreamMessage message)
    {
        _receivedFirstData = true;

        if (reader.ReadBoolean())
        {
            var refID = new RefID(reader.ReadUInt64());
            if (refID != _targetRefID)
            {
                _targetRefID = refID;
                _updateTarget = true;
            }
        }
        else if (_targetRefID != RefID.Null)
        {
            _targetRefID = RefID.Null;
            _updateTarget = true;
        }
    }
}
