using System;
namespace Lumora.Core.Assets.Audio;
public interface IAudioStaticAsset : IAudioAsset 
{
    public Span<byte> GetBytes();
}
