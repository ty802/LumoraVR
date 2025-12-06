using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Numerics;
using Lumora.Core.Logging;

namespace Lumora.Core.Audio;

/// <summary>
/// Global audio system managing audio playback, 3D spatialization, and audio processing.
/// </summary>
public class AudioSystem : IDisposable
{
    /// <summary>
    /// Helper method to clamp a float value between min and max.
    /// </summary>
    private static float Clamp(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
    private readonly List<AudioSource> _activeSources = new List<AudioSource>();
    private readonly Queue<AudioSource> _sourcePool = new Queue<AudioSource>();
    private readonly object _audioLock = new object();

    private bool _initialized = false;
    private float _masterVolume = 1.0f;
    private float _musicVolume = 1.0f;
    private float _effectsVolume = 1.0f;
    private float _voiceVolume = 1.0f;

    // Audio listener (usually attached to the camera/head)
    public AudioListener ActiveListener { get; set; }

    // Volume controls
    public float MasterVolume
    {
        get => _masterVolume;
        set => _masterVolume = Clamp(value, 0f, 1f);
    }

    public float MusicVolume
    {
        get => _musicVolume;
        set => _musicVolume = Clamp(value, 0f, 1f);
    }

    public float EffectsVolume
    {
        get => _effectsVolume;
        set => _effectsVolume = Clamp(value, 0f, 1f);
    }

    public float VoiceVolume
    {
        get => _voiceVolume;
        set => _voiceVolume = Clamp(value, 0f, 1f);
    }

    // Statistics
    public int ActiveSourceCount => _activeSources.Count;
    public int PooledSourceCount => _sourcePool.Count;

    /// <summary>
    /// Initialize the audio system.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        // Pre-allocate audio source pool
        for (int i = 0; i < 32; i++)
        {
            _sourcePool.Enqueue(new AudioSource());
        }

        _initialized = true;

        await Task.CompletedTask;
        Logger.Log($"AudioSystem: Initialized with {_sourcePool.Count} pooled sources");
    }

    /// <summary>
    /// Play a 2D audio clip (non-positional).
    /// </summary>
    public AudioSource Play2D(AudioClip clip, float volume = 1f, bool loop = false, AudioCategory category = AudioCategory.Effects)
    {
        if (clip == null)
            return null;

        var source = GetPooledSource();
        if (source == null)
            return null;

        source.Clip = clip;
        source.Volume = volume;
        source.Loop = loop;
        source.Is3D = false;
        source.Category = category;
        source.Play();

        lock (_audioLock)
        {
            _activeSources.Add(source);
        }

        return source;
    }

    /// <summary>
    /// Play a 3D audio clip at a position.
    /// </summary>
    public AudioSource Play3D(AudioClip clip, Vector3 position, float volume = 1f, float minDistance = 1f, float maxDistance = 100f, bool loop = false, AudioCategory category = AudioCategory.Effects)
    {
        if (clip == null)
            return null;

        var source = GetPooledSource();
        if (source == null)
            return null;

        source.Clip = clip;
        source.Position = position;
        source.Volume = volume;
        source.MinDistance = minDistance;
        source.MaxDistance = maxDistance;
        source.Loop = loop;
        source.Is3D = true;
        source.Category = category;
        source.Play();

        lock (_audioLock)
        {
            _activeSources.Add(source);
        }

        return source;
    }

    /// <summary>
    /// Play a one-shot audio clip (fire and forget).
    /// </summary>
    public void PlayOneShot(AudioClip clip, Vector3? position = null, float volume = 1f, AudioCategory category = AudioCategory.Effects)
    {
        if (position.HasValue)
        {
            Play3D(clip, position.Value, volume, 1f, 100f, false, category);
        }
        else
        {
            Play2D(clip, volume, false, category);
        }
    }

    /// <summary>
    /// Stop all playing audio sources.
    /// </summary>
    public void StopAll(AudioCategory? category = null)
    {
        lock (_audioLock)
        {
            foreach (var source in _activeSources)
            {
                if (!category.HasValue || source.Category == category.Value)
                {
                    source.Stop();
                }
            }
        }
    }

    /// <summary>
    /// Pause all playing audio sources.
    /// </summary>
    public void PauseAll(AudioCategory? category = null)
    {
        lock (_audioLock)
        {
            foreach (var source in _activeSources)
            {
                if (!category.HasValue || source.Category == category.Value)
                {
                    source.Pause();
                }
            }
        }
    }

    /// <summary>
    /// Resume all paused audio sources.
    /// </summary>
    public void ResumeAll(AudioCategory? category = null)
    {
        lock (_audioLock)
        {
            foreach (var source in _activeSources)
            {
                if (!category.HasValue || source.Category == category.Value)
                {
                    source.Resume();
                }
            }
        }
    }

    /// <summary>
    /// Update the audio system.
    /// </summary>
    public void Update(float deltaTime)
    {
        if (!_initialized)
            return;

        lock (_audioLock)
        {
            // Update active sources and return finished ones to pool
            for (int i = _activeSources.Count - 1; i >= 0; i--)
            {
                var source = _activeSources[i];

                source.Update(deltaTime);

                // Apply volume categories
                float categoryVolume = GetCategoryVolume(source.Category);
                source.EffectiveVolume = source.Volume * categoryVolume * _masterVolume;

                // Calculate 3D spatialization if needed
                if (source.Is3D && ActiveListener != null)
                {
                    Calculate3DParameters(source, ActiveListener);
                }

                // Return finished sources to pool
                if (!source.IsPlaying && !source.IsPaused)
                {
                    _activeSources.RemoveAt(i);
                    ReturnToPool(source);
                }
            }
        }
    }

    /// <summary>
    /// Calculate 3D audio parameters.
    /// </summary>
    private void Calculate3DParameters(AudioSource source, AudioListener listener)
    {
        // Calculate distance attenuation
        float distance = Vector3.Distance(source.Position, listener.Position);
        float attenuation = 1f;

        if (distance > source.MinDistance)
        {
            if (distance < source.MaxDistance)
            {
                // Linear falloff (could be replaced with logarithmic or custom curve)
                float range = source.MaxDistance - source.MinDistance;
                float distanceRatio = (distance - source.MinDistance) / range;
                attenuation = 1f - distanceRatio;
            }
            else
            {
                attenuation = 0f;
            }
        }

        source.DistanceAttenuation = attenuation;

        // Calculate panning (simplified stereo panning)
        Vector3 toSource = source.Position - listener.Position;
        if (toSource.Length() > 0.001f)
        {
            toSource = Vector3.Normalize(toSource);
            float dot = Vector3.Dot(listener.Right, toSource);
            source.Pan = dot; // -1 = left, 0 = center, 1 = right
        }
        else
        {
            source.Pan = 0f;
        }

        // Calculate doppler effect (if enabled)
        if (source.EnableDoppler && (source.Velocity.Length() > 0 || listener.Velocity.Length() > 0))
        {
            float speedOfSound = 343f; // m/s
            Vector3 sourceToListener = listener.Position - source.Position;
            float relativeVelocity = Vector3.Dot(source.Velocity - listener.Velocity, sourceToListener) / sourceToListener.Length();
            source.DopplerShift = 1f + (relativeVelocity / speedOfSound);
        }
        else
        {
            source.DopplerShift = 1f;
        }
    }

    /// <summary>
    /// Get volume for a specific category.
    /// </summary>
    private float GetCategoryVolume(AudioCategory category)
    {
        return category switch
        {
            AudioCategory.Music => _musicVolume,
            AudioCategory.Effects => _effectsVolume,
            AudioCategory.Voice => _voiceVolume,
            AudioCategory.UI => _effectsVolume,
            _ => 1f
        };
    }

    /// <summary>
    /// Get a source from the pool or create a new one.
    /// </summary>
    private AudioSource GetPooledSource()
    {
        lock (_audioLock)
        {
            if (_sourcePool.Count > 0)
            {
                return _sourcePool.Dequeue();
            }
        }

        // Pool empty, create new source
        Logger.Warn("AudioSystem: Source pool empty, creating new source");
        return new AudioSource();
    }

    /// <summary>
    /// Return a source to the pool.
    /// </summary>
    private void ReturnToPool(AudioSource source)
    {
        source.Reset();

        lock (_audioLock)
        {
            if (_sourcePool.Count < 64) // Max pool size
            {
                _sourcePool.Enqueue(source);
            }
        }
    }

    /// <summary>
    /// Dispose of the audio system.
    /// </summary>
    public void Dispose()
    {
        if (!_initialized)
            return;

        StopAll();

        lock (_audioLock)
        {
            _activeSources.Clear();
            _sourcePool.Clear();
        }

        _initialized = false;
        Logger.Log("AudioSystem: Disposed");
    }
}

/// <summary>
/// Audio categories for volume control.
/// </summary>
public enum AudioCategory
{
    Master,
    Music,
    Effects,
    Voice,
    UI
}

/// <summary>
/// Represents an audio source for playing audio clips.
/// </summary>
public class AudioSource
{
    public AudioClip Clip { get; set; }
    public float Volume { get; set; } = 1f;
    public float EffectiveVolume { get; set; } = 1f;
    public float Pitch { get; set; } = 1f;
    public bool Loop { get; set; }
    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }

    // 3D audio properties
    public bool Is3D { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }
    public float MinDistance { get; set; } = 1f;
    public float MaxDistance { get; set; } = 100f;
    public float Pan { get; set; } = 0f; // -1 to 1
    public float DistanceAttenuation { get; set; } = 1f;
    public bool EnableDoppler { get; set; }
    public float DopplerShift { get; set; } = 1f;

    // Playback state
    public float PlaybackPosition { get; private set; }
    public AudioCategory Category { get; set; }

    public void Play()
    {
        if (Clip == null)
            return;

        IsPlaying = true;
        IsPaused = false;
        PlaybackPosition = 0f;
    }

    public void Stop()
    {
        IsPlaying = false;
        IsPaused = false;
        PlaybackPosition = 0f;
    }

    public void Pause()
    {
        if (IsPlaying)
        {
            IsPlaying = false;
            IsPaused = true;
        }
    }

    public void Resume()
    {
        if (IsPaused)
        {
            IsPlaying = true;
            IsPaused = false;
        }
    }

    public void Update(float deltaTime)
    {
        if (!IsPlaying || Clip == null)
            return;

        // Update playback position
        PlaybackPosition += deltaTime * Pitch * DopplerShift;

        // Check if finished
        if (PlaybackPosition >= Clip.Duration)
        {
            if (Loop)
            {
                PlaybackPosition %= Clip.Duration;
            }
            else
            {
                Stop();
            }
        }
    }

    public void Reset()
    {
        Clip = null;
        Volume = 1f;
        Pitch = 1f;
        Loop = false;
        IsPlaying = false;
        IsPaused = false;
        Is3D = false;
        Position = Vector3.Zero;
        Velocity = Vector3.Zero;
        MinDistance = 1f;
        MaxDistance = 100f;
        Pan = 0f;
        DistanceAttenuation = 1f;
        EnableDoppler = false;
        DopplerShift = 1f;
        PlaybackPosition = 0f;
        Category = AudioCategory.Effects;
    }
}

/// <summary>
/// Represents an audio listener (usually attached to camera).
/// </summary>
public class AudioListener
{
    public Vector3 Position { get; set; }
    public Vector3 Forward { get; set; } = Vector3.UnitZ;
    public Vector3 Up { get; set; } = Vector3.UnitY;
    public Vector3 Right => Vector3.Cross(Forward, Up);
    public Vector3 Velocity { get; set; }
}

/// <summary>
/// Represents an audio clip asset.
/// </summary>
public class AudioClip
{
    public string Name { get; set; }
    public float Duration { get; set; }
    public int SampleRate { get; set; } = 44100;
    public int Channels { get; set; } = 2;
    public byte[] AudioData { get; set; }
}