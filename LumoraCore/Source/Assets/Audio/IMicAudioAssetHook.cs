using System;
using Lumora.Core.Assets;
namespace Lumora.Core.Assets.Audio;

public interface IMicAudioAssetHook : IStreamableAudioAssetHook
{
    IAudioInput[] GetAvailableInputs();
}

