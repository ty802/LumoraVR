using System;
namespace Lumora.Core.Assets.Audio;

public interface IAudioAsset
{
    public string Codec { get; }
    public int Channels { get; }
}
