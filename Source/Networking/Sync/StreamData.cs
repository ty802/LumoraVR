using System;
using System.IO;
using Godot;

namespace Aquamarine.Source.Networking.Sync;

/// <summary>
/// Stream data types for high-frequency continuous data.
/// 
/// </summary>
public enum StreamType : int
{
    HeadTransform = 0,
    LeftHandTransform = 1,
    RightHandTransform = 2,
    Audio = 3,
    EyeTracking = 4,
    FaceTracking = 5
}

/// <summary>
/// Transform stream data (position, rotation, scale).
/// </summary>
public struct TransformStreamData
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;

    public byte[] Encode()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Position
        writer.Write(Position.X);
        writer.Write(Position.Y);
        writer.Write(Position.Z);

        // Rotation (quaternion)
        writer.Write(Rotation.X);
        writer.Write(Rotation.Y);
        writer.Write(Rotation.Z);
        writer.Write(Rotation.W);

        // Scale
        writer.Write(Scale.X);
        writer.Write(Scale.Y);
        writer.Write(Scale.Z);

        return ms.ToArray();
    }

    public static TransformStreamData Decode(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        return new TransformStreamData
        {
            Position = new Vector3(
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle()
            ),
            Rotation = new Quaternion(
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle()
            ),
            Scale = new Vector3(
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle()
            )
        };
    }
}

/// <summary>
/// User stream bag - holds all stream data for a user.
/// 
/// </summary>
public class UserStreamBag
{
    public TransformStreamData? HeadTransform { get; set; }
    public TransformStreamData? LeftHandTransform { get; set; }
    public TransformStreamData? RightHandTransform { get; set; }

    public bool HasData => HeadTransform.HasValue ||
                          LeftHandTransform.HasValue ||
                          RightHandTransform.HasValue;

    public void Clear()
    {
        HeadTransform = null;
        LeftHandTransform = null;
        RightHandTransform = null;
    }
}
