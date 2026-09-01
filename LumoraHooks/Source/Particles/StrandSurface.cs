// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Godot;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;

namespace Lumora.Godot.Hooks.Particles;

// One strand output on screen: an ArrayMesh rebuilt in place, a MeshInstance3D holding it, and the
// buffers both are fed from.
//
// WHAT GODOT'S API FORCES. AddSurfaceFromArrays takes the vertex count from the LENGTH of the arrays
// handed to it, so a buffer cannot simply be over-allocated and partially filled the way the
// simulation's are. Reallocating every frame as a trail grows and shrinks by a few vertices is exactly
// the per-frame garbage the sim layer went to some trouble to avoid, so the arrays are bucketed
// instead: they round up to a block, the tail is padded with degenerate triangles that cost a
// discarded primitive each, and a frame inside the current block allocates nothing at all. The
// Godot.Collections.Array wrapper is built once and reused; assigning a managed array into it still
// marshals a copy into native memory, and there is no public API that does not. -xlinka
internal sealed class StrandSurface : IDisposable
{
    private const string ShaderPath = "res://Shaders/EngineStrand.gdshader";

    // Vertex block. Small enough that a short trail is not paying for thousands of dead vertices,
    // large enough that a fountain's strand count wandering by a few does not cross it.
    private const int VertexBlock = 512;

    // Has to divide by six, not just by three: Godot rejects a triangle surface whose index count is
    // not a whole number of triangles, and the padding is written a quad at a time.
    private const int IndexBlock = 768;

    private readonly StrandGeometryBuilder _builder = new();
    private readonly global::Godot.Collections.Array _arrays = new();

    private ArrayMesh _mesh = null!;
    private MeshInstance3D _instance = null!;
    private ShaderMaterial _material = null!;

    private Vector3[] _positions = System.Array.Empty<Vector3>();
    private Color[] _colors = System.Array.Empty<Color>();
    private Vector2[] _uvs = System.Array.Empty<Vector2>();
    private int[] _indices = System.Array.Empty<int>();

    private StrandRebuildGate _gate;
    private bool _hadGeometry;

    private float _appliedEmission = float.NaN;
    private int _appliedQueue = int.MinValue;
    private Texture2D? _appliedTexture;
    private bool _materialApplied;

    public StrandSurface(Node3D parent, string name)
    {
        _material = new ShaderMaterial();
        if (ResourceLoader.Exists(ShaderPath))
            _material.Shader = GD.Load<Shader>(ShaderPath);
        else
            Lumora.Core.Logging.Logger.Warn($"StrandSurface: strand shader not found at {ShaderPath}");

        _mesh = new ArrayMesh();
        _instance = new MeshInstance3D
        {
            Name = name,
            Mesh = _mesh,
            // Strands are additive streaks. Casting from them costs a full shadow pass over geometry
            // that is rebuilt every frame and reads as a smear anyway.
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _arrays.Resize((int)Mesh.ArrayType.Max);
        parent.AddChild(_instance);
    }

    public MeshInstance3D Instance => _instance;

    public bool Visible
    {
        get => _instance != null && GodotObject.IsInstanceValid(_instance) && _instance.Visible;
        set
        {
            if (_instance != null && GodotObject.IsInstanceValid(_instance))
                _instance.Visible = value;
        }
    }

    // Called every frame, and every shader parameter write is a marshalled round trip, so this only
    // touches the material when something it depends on has actually moved. The texture is compared by
    // reference because an asset that finishes loading later hands back a different Texture2D and there
    // is no change notification to hang off. -xlinka
    public void ApplyMaterial(float emissionStrength, int renderQueue, IAssetProvider<TextureAsset>? texture)
    {
        if (_material == null)
            return;

        Texture2D? godotTexture = null;
        if (texture?.Asset?.Hook is IGodotTexture hook && hook.IsValid)
            godotTexture = hook.GodotTexture2D;

        if (_appliedEmission == emissionStrength && _appliedQueue == renderQueue
            && ReferenceEquals(_appliedTexture, godotTexture) && _materialApplied)
            return;

        _appliedEmission = emissionStrength;
        _appliedQueue = renderQueue;
        _appliedTexture = godotTexture;
        _materialApplied = true;

        _material.RenderPriority = System.Math.Clamp(renderQueue, -128, 127);
        _material.SetShaderParameter("emission_strength", emissionStrength);
        _material.SetShaderParameter("strand_texture", godotTexture!);
        _material.SetShaderParameter("use_texture", godotTexture != null);
    }

    public void ApplyLodRange(in LodVisibilityRange range) => range.ApplyTo(_instance);

    public void Invalidate()
    {
        _gate.Reset();
    }

    // Rebuild the ribbon unless nothing that shapes it has moved. Returns false when the frame was
    // skipped, which is the common case for a velocity-aligned strand whose system has gone quiet.
    public bool Rebuild(
        ReadOnlySpan<float3> positions,
        ReadOnlySpan<colorHDR> colors,
        ReadOnlySpan<float> widths,
        ReadOnlySpan<StrandRange> strands,
        int strandCount,
        in StrandGeometryParams parameters,
        in float3 viewPosition,
        int version)
    {
        if (_mesh == null || !GodotObject.IsInstanceValid(_mesh))
            return false;

        bool cameraFacing = parameters.Alignment == StrandAlignment.CameraFacing;
        if (!_gate.ShouldRebuild(version, viewPosition, cameraFacing))
            return false;

        StrandGeometryBuilder.Measure(strands, strandCount, parameters.Smoothing, out int maxVertices, out int maxIndices);
        if (maxVertices < 3 || maxIndices < 3)
        {
            ClearGeometry();
            return true;
        }

        EnsureBuffers(maxVertices, maxIndices);

        var sink = new Sink(_positions, _colors, _uvs, _indices);
        _builder.Build(positions, colors, widths, strands, strandCount, parameters, viewPosition,
            ref sink, out int vertexCount, out int indexCount);

        if (vertexCount < 3 || indexCount < 3)
        {
            ClearGeometry();
            return true;
        }

        Pad(vertexCount, indexCount);
        Upload();
        return true;
    }

    public void ClearGeometry()
    {
        if (!_hadGeometry)
            return;
        _hadGeometry = false;
        if (_mesh != null && GodotObject.IsInstanceValid(_mesh))
            _mesh.ClearSurfaces();
    }

    private void Upload()
    {
        _mesh.ClearSurfaces();
        _arrays[(int)Mesh.ArrayType.Vertex] = _positions;
        _arrays[(int)Mesh.ArrayType.Color] = _colors;
        _arrays[(int)Mesh.ArrayType.TexUV] = _uvs;
        _arrays[(int)Mesh.ArrayType.Index] = _indices;
        _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, _arrays);
        _mesh.SurfaceSetMaterial(0, _material);
        _hadGeometry = true;
    }

    // The tail of the block. Vertices repeat the last real one so the surface AABB stays honest -
    // padding at the origin would drag the bounds back to the slot and defeat frustum culling - and
    // the spare indices are degenerate triangles, which the rasteriser discards before shading.
    private void Pad(int vertexCount, int indexCount)
    {
        var last = _positions[vertexCount - 1];
        var uv = _uvs[vertexCount - 1];
        for (int i = vertexCount; i < _positions.Length; i++)
        {
            _positions[i] = last;
            _colors[i] = new Color(0f, 0f, 0f, 0f);
            _uvs[i] = uv;
        }
        for (int i = indexCount; i < _indices.Length; i++)
            _indices[i] = 0;
    }

    private void EnsureBuffers(int vertices, int indices)
    {
        int wantVertices = RoundUp(vertices, VertexBlock);
        int wantIndices = RoundUp(indices, IndexBlock);

        // Grow on demand, shrink only once the peak is well behind us. Resizing on every wobble is the
        // allocation this whole scheme exists to avoid.
        if (_positions.Length < wantVertices || _positions.Length > wantVertices * 4)
        {
            _positions = new Vector3[wantVertices];
            _colors = new Color[wantVertices];
            _uvs = new Vector2[wantVertices];
        }
        if (_indices.Length < wantIndices || _indices.Length > wantIndices * 4)
            _indices = new int[wantIndices];
    }

    private static int RoundUp(int count, int block) => ((System.Math.Max(count, 1) + block - 1) / block) * block;

    public void Dispose()
    {
        if (_instance != null && GodotObject.IsInstanceValid(_instance))
            _instance.QueueFree();
        _mesh?.Dispose();
        _material?.Dispose();
        _instance = null!;
        _mesh = null!;
        _material = null!;
    }

    private readonly struct Sink : IStrandVertexSink
    {
        private readonly Vector3[] _positions;
        private readonly Color[] _colors;
        private readonly Vector2[] _uvs;
        private readonly int[] _indices;

        public Sink(Vector3[] positions, Color[] colors, Vector2[] uvs, int[] indices)
        {
            _positions = positions;
            _colors = colors;
            _uvs = uvs;
            _indices = indices;
        }

        public void Vertex(int index, in float3 position, in colorHDR color, float u, float v)
        {
            _positions[index] = new Vector3(position.x, position.y, position.z);
            _colors[index] = new Color(color.r, color.g, color.b, color.a);
            _uvs[index] = new Vector2(u, v);
        }

        public void Triangle(int index, int a, int b, int c)
        {
            _indices[index] = a;
            _indices[index + 1] = b;
            _indices[index + 2] = c;
        }
    }
}
