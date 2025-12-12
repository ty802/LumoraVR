using System;
using System.Threading.Tasks;
namespace Lumora.Core.Assets.Audio;

public interface IStreamableAudioAsset : IStreamableAsset,IAudioAsset
{
    public Span<byte> GetSample();
}
