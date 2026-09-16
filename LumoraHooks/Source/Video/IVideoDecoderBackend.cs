// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Godot;
using Lumora.Core.Media;

namespace Lumora.Godot.Hooks.Video;

// Where a decoder hands its picture. The hook implements this and owns the GPU texture, so a backend
// never has to know whether it is feeding a material, a UI quad or nothing at all. Both calls happen
// on the main thread from inside the backend's Tick. -xlinka
public interface IVideoFrameTarget
{
    // rgba is tightly packed RGBA8 rows, at least width*height*4 bytes; anything past that is padding
    // the target ignores. The backend may still own the array, so the target copies out of it before
    // returning.
    void SubmitFrame(byte[] rgba, int width, int height);

    // For a backend that already owns a Godot texture and would only lose time being read back into
    // ours. The object must stay the same for the life of the backend.
    void PresentTexture(Texture2D? texture, int width, int height);
}

// One decoder. The hook picks an implementation per Load and drives it from the main thread; the
// decoder itself runs on whatever threads it likes and only publishes through Tick. -xlinka
public interface IVideoDecoderBackend : IDisposable
{
    // MediaServices.EngineNative / MediaServices.EngineLibVlc.
    string EngineName { get; }

    // Seconds the decoder is ahead (+) or behind (-) the position the core last asked for.
    float CurrentClockError { get; }

    VideoStats Stats { get; }

    event Action<VideoReadyInfo>? Ready;
    event Action<string>? Failed;

    void Open(string source, string? audioSource, int? audioTrackIndex, bool isStream, bool isLive, int maxWidth, int maxHeight);

    void UpdateState(bool play, bool loop, double position, float speed);

    void SetVolume(float volume);
    void SetAudioTrack(int? index);
    void SetAudioSink(IVideoAudioSink? sink);

    // Main thread, once per frame: publish a decoded frame if one is waiting and apply whatever the
    // last UpdateState asked for. Nothing else touches Godot.
    void Tick(IVideoFrameTarget target);

    void Stop();
}
