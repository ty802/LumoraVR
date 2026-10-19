// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Godot;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks;

// One compiled Godot shader per custom source text, shared by every material showing it. Fifty copies of
// an imported orb are one compile, and a shader graph's node previews share whatever they have in common
// with each other and with the material. The most recently used are kept; a shader dropped from here stays
// alive for as long as a material still wears it. Room for a few hundred, so a graph closed and opened
// again finds its previews still compiled. -xlinka
//
// Built off the main thread. Godot makes a shader the first time something asks for its RID, on whichever
// thread asks, when the renderer can make resources away from its own thread (Forward+ and Mobile can).
// That first ask parses the source and reads Godot's own disk cache of what it compiled before, and the
// cache read was the expensive part: 13 to 59 ms a shader on the main thread, against 2 to 7 ms for a
// source it had never seen, and a graph opening asked for two hundred of them. Two threads take them in
// the order asked and warm each one up before the frame gets it. -xlinka
//
// Two lanes. What a material wears goes first; a card's node preview waits until no material is waiting,
// so a graph opening with two hundred previews never stands between a world's materials and the shaders they wear.
// A preview's source that a material asks for as well moves up to the first lane. -xlinka
internal static class CustomShaderCache
{
    private const int Capacity = 384;
    private const int Builders = 2;

    // Main thread only.
    private static readonly Dictionary<string, LinkedListNode<(string Source, Shader Shader)>> _bySource = new(StringComparer.Ordinal);
    private static readonly LinkedList<(string Source, Shader Shader)> _recent = new();
    private static readonly Dictionary<string, List<Action<Shader>>> _waiting = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _waitingFirst = new(StringComparer.Ordinal);
    private static bool _pumping;
    private static bool _buildersStarted;

    private static readonly BlockingCollection<string> _first = new(new ConcurrentQueue<string>());
    private static readonly BlockingCollection<string> _later = new(new ConcurrentQueue<string>());
    private static readonly BlockingCollection<string>[] _lanes = { _first, _later };
    // Sources asked for and not taken by a builder yet. One moved up sits in both lanes: whichever builder
    // reaches it first takes it, and the other finds it gone.
    private static readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.Ordinal);
    private static readonly ConcurrentQueue<(string Source, Shader? Shader)> _built = new();
    private static int _pending;

    static CustomShaderCache()
    {
        Lumora.Core.Assets.ShaderBuildStatus.PendingProvider = () => Pending;
    }

    // Asked for and not handed over yet: waiting for a builder, building, or built and waiting for the frame.
    public static int Pending => Volatile.Read(ref _pending);

    // The shader for this source if it is compiled. Otherwise null, and ready hears it on a later frame.
    // Late is for a card's node preview: it builds once no material is waiting.
    public static Shader? Get(string source, Action<Shader> ready, bool late = false)
    {
        if (TryCached(source, out var shader))
            return shader;

        if (!_waiting.TryGetValue(source, out var callbacks))
        {
            callbacks = new List<Action<Shader>>();
            _waiting[source] = callbacks;
            Interlocked.Increment(ref _pending);
            StartBuilders();
            _queued[source] = 0;
            if (late)
            {
                _later.Add(source);
            }
            else
            {
                _waitingFirst.Add(source);
                _first.Add(source);
            }
        }
        else if (!late && _waitingFirst.Add(source) && _queued.ContainsKey(source))
        {
            _first.Add(source);
        }
        callbacks.Add(ready);
        StartPumping();
        return null;
    }

    private static bool TryCached(string source, out Shader shader)
    {
        shader = null!;
        if (!_bySource.TryGetValue(source, out var known))
            return false;
        _recent.Remove(known);
        if (!GodotObject.IsInstanceValid(known.Value.Shader))
        {
            _bySource.Remove(source);
            return false;
        }
        _recent.AddFirst(known);
        shader = known.Value.Shader;
        return true;
    }

    private static void Keep(string source, Shader shader)
    {
        if (_bySource.Remove(source, out var stale))
            _recent.Remove(stale);
        _bySource[source] = _recent.AddFirst((source, shader));
        while (_recent.Count > Capacity)
        {
            var oldest = _recent.Last!;
            _recent.RemoveLast();
            _bySource.Remove(oldest.Value.Source);
        }
    }

    private static void StartBuilders()
    {
        if (_buildersStarted)
            return;
        _buildersStarted = true;
        PrepareWarmSurface();
        for (int i = 0; i < Builders; i++)
            new Thread(Build) { IsBackground = true, Name = $"Shader build {i + 1}" }.Start();
    }

    private static void Build()
    {
        while (true)
        {
            BlockingCollection<string>.TakeFromAny(_lanes, out var source);
            if (source == null || !_queued.TryRemove(source, out _))
                continue;
            Shader? shader = null;
            try
            {
                shader = new Shader { Code = source };
                shader.GetRid();
                Warm(shader);
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"CustomShaderCache: building a shader failed: {ex.Message}");
                shader = null;
            }
            _built.Enqueue((source, shader));
        }
    }

    // One triangle's surface as Godot itself lays it out, made once on the main thread: positions, normals,
    // tangents, colors and one UV set, the channels a procedural mesh carries.
    private static global::Godot.Collections.Dictionary? _warmSurface;

    private static void PrepareWarmSurface()
    {
        try
        {
            var arrays = new global::Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f) };
            arrays[(int)Mesh.ArrayType.Normal] = new[] { Vector3.Back, Vector3.Back, Vector3.Back };
            arrays[(int)Mesh.ArrayType.Tangent] = new[] { 1f, 0f, 0f, 1f, 1f, 0f, 0f, 1f, 1f, 0f, 0f, 1f };
            arrays[(int)Mesh.ArrayType.Color] = new[] { Colors.White, Colors.White, Colors.White };
            arrays[(int)Mesh.ArrayType.TexUV] = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f) };
            arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2 };
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            var surfaces = mesh.Get("_surfaces").AsGodotArray();
            if (surfaces.Count > 0)
                _warmSurface = surfaces[0].AsGodotDictionary();
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"CustomShaderCache: no warm-up surface, first draws will compile: {ex.Message}");
        }
    }

    // The shader on a throwaway triangle, made on this thread. A mesh made away from the render thread makes
    // Godot wait right there for the shader's variants and for the pipelines a surface like this one needs,
    // instead of the frame that first draws it waiting for them. It also keeps two shaders' variants in
    // Godot's worker pool at a time: twenty handed over unwarmed queued hundreds of variant compiles ahead
    // of everything the frame itself was waiting on. -xlinka
    private static void Warm(Shader shader)
    {
        if (_warmSurface == null)
            return;
        var material = new ShaderMaterial { Shader = shader };
        var surface = _warmSurface.Duplicate();
        surface["material"] = material;
        var mesh = new ArrayMesh();
        mesh.Set("_surfaces", new global::Godot.Collections.Array { surface });
        mesh.Dispose();
        material.Dispose();
    }

    private static void StartPumping()
    {
        if (_pumping || global::Godot.Engine.GetMainLoop() is not SceneTree tree)
            return;
        _pumping = true;
        tree.ProcessFrame += Pump;
    }

    // Every shader finished since the last frame, handed to everything that asked for it.
    private static void Pump()
    {
        while (_built.TryDequeue(out var built))
        {
            if (_waiting.Remove(built.Source, out var callbacks))
                Interlocked.Decrement(ref _pending);
            _waitingFirst.Remove(built.Source);
            if (built.Shader == null)
                continue;
            Keep(built.Source, built.Shader);
            if (callbacks == null)
                continue;
            foreach (var ready in callbacks)
                ready(built.Shader);
        }

        if (_waiting.Count == 0 && global::Godot.Engine.GetMainLoop() is SceneTree tree)
        {
            tree.ProcessFrame -= Pump;
            _pumping = false;
        }
    }
}
