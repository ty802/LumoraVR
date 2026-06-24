// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Networking.Streams;

/// <summary>
/// Synchronized Dictionary of streams for a user.
/// Streams are synced over the network so clients receive streams with matching RefIDs.
/// User handles initialization via OnElementAdded event.
/// </summary>
public class UserStreamStorage : ReplicatedObjectCollection<Stream>
{
    /// <summary>
    /// The user that owns the streams contained here.
    /// </summary>
    public User Owner { get; private set; } = null!;

    public UserStreamStorage()
    {
    }

    /// <summary>
    /// Initialize the storage for a given user
    /// </summary>
    public void Initialize(User user)
    {
        Owner = user;
        base.Initialize(user.World, user);
    }

    /// <summary>
    /// All streams stored here.
    /// </summary>
    public System.Collections.Generic.IEnumerable<Stream> Streams => Values;

    /// <summary>
    /// Update all streams.
    /// </summary>
    public void Update()
    {
        foreach (var stream in Values)
        {
            if (stream.Active)
            {
                stream.Update();
            }
        }
    }

    protected override void EncodeElement(BinaryWriter writer, Stream element)
    {
        var typeName = element.GetType().FullName;
        writer.Write(typeName ?? "");
    }

    protected override Stream DecodeElement(BinaryReader reader)
    {
        throw new InvalidOperationException("UserStreamStorage requires CreateElementWithKey");
    }

    protected override void SkipElement(BinaryReader reader)
    {
        reader.ReadString();
    }

    protected override Stream CreateElementWithKey(RefID key, BinaryReader reader)
    {
        return InstantiateStream(reader.ReadString());
    }

    // Reconstruct a received stream from its type name. Resolved by reflection so any stream subtype
    // replicates, but guarded: the resolved type must be a concrete Stream subclass with a public
    // parameterless ctor, else we reject it - the network can't make us instantiate arbitrary types. -xlinka
    private static Stream InstantiateStream(string typeName)
    {
        var type = ResolveStreamType(typeName);
        if (type == null)
            throw new InvalidOperationException($"Rejected unknown or invalid stream type: '{typeName}'");

        return (Stream)Activator.CreateInstance(type)!;
    }

    private static Type? ResolveStreamType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return null;

        // Calling assembly + corelib first (covers the built-in streams), then loaded assemblies.
        var type = Type.GetType(typeName) ?? typeof(Stream).Assembly.GetType(typeName);
        if (type == null)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(typeName);
                if (type != null)
                    break;
            }
        }

        if (type == null || type.IsAbstract
            || !typeof(Stream).IsAssignableFrom(type)
            || type.GetConstructor(Type.EmptyTypes) == null)
        {
            return null;
        }

        return type;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
