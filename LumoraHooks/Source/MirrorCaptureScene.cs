// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Godot;
using Lumora.Godot.Helpers;

namespace Lumora.Godot.Hooks;

// Godot shares directional shadows between viewports of a scenario. A private scenario lets a
// mirror switch off all shadows and select users without changing the scene seen by other cameras.
// Instances share mesh, skin, particle and material RIDs; animation/physics are never duplicated.
internal sealed class MirrorCaptureScene : IDisposable
{
    internal const uint CaptureMask = unchecked((uint)(RenderHelper.PUBLIC_RENDER_MASK
        & ~RenderHelper.FREECAM_INDICATOR_LAYER & ~RenderHelper.MIRROR_LAYER));
    public World3D World { get; } = new();
    private readonly Dictionary<VisualInstance3D, CaptureInstance> _instances = new();
    private readonly HashSet<Node> _userRoots = new();
    private readonly List<VisualInstance3D> _removed = new();
    private double _nextScan;
    private bool _usersOnly, _noLights, _noShadows;
    private int _generation;
    private bool _disposed;

    // A moving body may draw its renderers past their nodes; their poses come from DirectPoses while it does.
    public MirrorCaptureScene() => DirectPoses.AddReader();

    public void Update(Node3D worldRoot, IReadOnlyCollection<Node3D> userRoots, bool usersOnly, bool noShadows, bool noLights)
    {
        double now = Time.GetTicksMsec() * 0.001;
        bool usersChanged = userRoots.Count != _userRoots.Count;
        foreach (var root in userRoots) usersChanged |= !_userRoots.Contains(root);
        bool scan = now >= _nextScan || usersChanged || usersOnly != _usersOnly || noLights != _noLights;
        bool refreshProperties = scan || noShadows != _noShadows;
        _noShadows = noShadows;
        if (scan)
        {
            _usersOnly = usersOnly;
            _noLights = noLights;
            _nextScan = now + 0.25;
            _generation++;
            _userRoots.Clear();
            foreach (var root in userRoots) _userRoots.Add(root);
            Scan(worldRoot, null);

            _removed.Clear();
            foreach (var (source, instance) in _instances)
                if (instance.Generation != _generation) _removed.Add(source);
            foreach (var source in _removed)
            {
                _instances[source].Dispose();
                _instances.Remove(source);
            }
        }

        foreach (var (source, instance) in _instances)
        {
            if (!GodotObject.IsInstanceValid(source) || !source.IsInsideTree()
                || (usersOnly && source is not Light3D
                    && (instance.UserRoot == null || !GodotObject.IsInstanceValid(instance.UserRoot)
                        || !instance.UserRoot.IsAncestorOf(source))))
            {
                instance.Hide();
                continue;
            }
            instance.Sync(source, noShadows, noLights, refreshProperties);
        }
    }

    private void Scan(Node node, Node3D? userRoot)
    {
        if (node is SubViewport) return;
        if (_userRoots.Contains(node)) userRoot = (Node3D)node;
        if (node is VisualInstance3D visual && (visual.Layers & CaptureMask) != 0
            && (visual is Light3D ? !_noLights : !_usersOnly || userRoot != null))
        {
            // Geometry plus probes/decals. World geometry, fog and sky do not enter users-only mode.
            if (visual is GeometryInstance3D or Light3D or ReflectionProbe or Decal or FogVolume or VoxelGI)
            {
                if (!_instances.TryGetValue(visual, out var instance))
                {
                    instance = new CaptureInstance(visual, World.Scenario);
                    _instances.Add(visual, instance);
                }
                instance.Generation = _generation;
                instance.UserRoot = userRoot;
            }
        }
        for (int i = 0, count = node.GetChildCount(); i < count; i++)
            Scan(node.GetChild(i), userRoot);
    }

    public void Dispose()
    {
        foreach (var instance in _instances.Values) instance.Dispose();
        _instances.Clear();
        World.Dispose();
        if (!_disposed)
        {
            _disposed = true;
            DirectPoses.RemoveReader();
        }
    }

    private sealed class CaptureInstance : IDisposable
    {
        public Rid Rid { get; }
        public int Generation;
        public Node3D? UserRoot;
        private Rid _base;
        private readonly Light3D? _light;
        private readonly List<StringName> _lightProperties = new();
        private readonly List<StringName> _shaderParameters = new();
        private Material? _override;
        private Mesh? _mesh;
        private bool _parametersKnown;
        private Rid _skeleton;
        private Transform3D _transform;
        private uint _layers = uint.MaxValue;
        private bool? _visible;
        private float[] _blendWeights = Array.Empty<float>();

        public CaptureInstance(VisualInstance3D source, Rid scenario)
        {
            if (source is Light3D light)
            {
                _light = light switch
                {
                    DirectionalLight3D => new DirectionalLight3D(),
                    SpotLight3D => new SpotLight3D(),
                    _ => new OmniLight3D(),
                };
                foreach (var property in light.GetPropertyList())
                {
                    string name = property["name"].AsString();
                    if ((property["usage"].AsInt32() & (int)PropertyUsageFlags.Storage) != 0
                        && (name.StartsWith("light_", StringComparison.Ordinal)
                            || name.StartsWith("shadow_", StringComparison.Ordinal)
                            || name.StartsWith("directional_", StringComparison.Ordinal)
                            || name.StartsWith("omni_", StringComparison.Ordinal)
                            || name.StartsWith("spot_", StringComparison.Ordinal)
                            || name.StartsWith("distance_fade_", StringComparison.Ordinal))
                        && name != "shadow_enabled")
                        _lightProperties.Add(new StringName(name));
                }
            }
            // Light3D assigns its base directly on its instance; VisualInstance3D.GetBase() is
            // empty for lights. Use the new light's own instance in the private scenario.
            Rid = _light?.GetInstance() ?? RenderingServer.InstanceCreate();
            RenderingServer.InstanceSetScenario(Rid, scenario);
        }

        public void Sync(VisualInstance3D source, bool noShadows, bool noLights, bool refreshProperties)
        {
            bool visible = source.IsVisibleInTree()
                && (source.Layers & CaptureMask) != 0 && !(noLights && _light != null)
                && !(noShadows && source is GeometryInstance3D { CastShadow: GeometryInstance3D.ShadowCastingSetting.ShadowsOnly });
            if (_visible != visible) RenderingServer.InstanceSetVisible(Rid, visible);
            _visible = visible;
            uint layers = source.Layers & CaptureMask;
            if (_layers != layers) RenderingServer.InstanceSetLayerMask(Rid, layers);
            _layers = layers;
            if (!DirectPoses.Any || !DirectPoses.TryGet(source.GetInstanceId(), out var transform))
                transform = source.GlobalTransform;
            if (_transform != transform) RenderingServer.InstanceSetTransform(Rid, transform);
            _transform = transform;
            var newBase = source.GetBase();
            if (_light == null && newBase != _base)
            {
                _base = newBase;
                RenderingServer.InstanceSetBase(Rid, newBase);
                _blendWeights = Array.Empty<float>();
                _parametersKnown = false;
                _skeleton = default;
                refreshProperties = true;
            }
            if (_light != null && source is Light3D light)
            {
                foreach (var property in _lightProperties)
                {
                    var value = light.Get(property);
                    if (!_light.Get(property).Equals(value)) _light.Set(property, value);
                }
                bool shadow = !noShadows && !noLights && light.ShadowEnabled;
                if (_light.ShadowEnabled != shadow) _light.ShadowEnabled = shadow;
            }
            if (source is not GeometryInstance3D geometry) return;
            // Transforms, skinning, blend shapes and shader animation stay live every frame. Static
            // draw configuration is refreshed with discovery, avoiding dozens of native calls per
            // object per eye frame in a large world. Shared material resources update themselves.
            if (refreshProperties)
            {
                RenderingServer.InstanceGeometrySetCastShadowsSetting(Rid, noShadows
                    ? RenderingServer.ShadowCastingSetting.Off
                    : (RenderingServer.ShadowCastingSetting)geometry.CastShadow);
                RenderingServer.InstanceGeometrySetMaterialOverride(Rid, geometry.MaterialOverride?.GetRid() ?? default);
                RenderingServer.InstanceGeometrySetMaterialOverlay(Rid, geometry.MaterialOverlay?.GetRid() ?? default);
                RenderingServer.InstanceGeometrySetTransparency(Rid, geometry.Transparency);
                RenderingServer.InstanceSetCustomAabb(Rid, geometry.CustomAabb);
                RenderingServer.InstanceSetExtraVisibilityMargin(Rid, geometry.ExtraCullMargin);
                RenderingServer.InstanceGeometrySetLodBias(Rid, geometry.LodBias);
                RenderingServer.InstanceGeometrySetFlag(Rid, RenderingServer.InstanceFlags.UseBakedLight,
                    geometry.GIMode == GeometryInstance3D.GIModeEnum.Static);
                RenderingServer.InstanceGeometrySetFlag(Rid, RenderingServer.InstanceFlags.UseDynamicGI,
                    geometry.GIMode == GeometryInstance3D.GIModeEnum.Dynamic);
                RenderingServer.InstanceGeometrySetVisibilityRange(Rid, geometry.VisibilityRangeBegin,
                    geometry.VisibilityRangeEnd, geometry.VisibilityRangeBeginMargin, geometry.VisibilityRangeEndMargin,
                    (RenderingServer.VisibilityRangeFadeMode)geometry.VisibilityRangeFadeMode);

                var mesh = (source as MeshInstance3D)?.Mesh;
                if (!_parametersKnown || _override != geometry.MaterialOverride || _mesh != mesh)
                {
                    _override = geometry.MaterialOverride;
                    _mesh = mesh;
                    _parametersKnown = true;
                    foreach (var name in _shaderParameters) name.Dispose();
                    _shaderParameters.Clear();
                    foreach (var property in RenderingServer.InstanceGeometryGetShaderParameterList(source.GetInstance()))
                        _shaderParameters.Add(property["name"].AsStringName());
                }
                if (source is MeshInstance3D { Mesh: not null } configuredMesh)
                {
                    for (int i = 0; i < configuredMesh.GetSurfaceOverrideMaterialCount(); i++)
                        RenderingServer.InstanceSetSurfaceOverrideMaterial(Rid, i,
                            configuredMesh.GetSurfaceOverrideMaterial(i)?.GetRid() ?? default);
                    int count = (int)RenderingServer.MeshGetBlendShapeCount(configuredMesh.Mesh.GetRid());
                    if (_blendWeights.Length != count)
                    {
                        _blendWeights = new float[count];
                        Array.Fill(_blendWeights, float.NaN);
                    }
                }
            }
            foreach (var parameter in _shaderParameters)
                RenderingServer.InstanceGeometrySetShaderParameter(Rid, parameter, geometry.GetInstanceShaderParameter(parameter));

            if (source is MeshInstance3D { Mesh: not null } meshInstance)
            {
                // SkinReference includes the mesh's bind poses; using Skeleton3D's RID loses them.
                var skeleton = meshInstance.GetSkinReference()?.GetSkeleton() ?? default;
                if (_skeleton != skeleton) RenderingServer.InstanceAttachSkeleton(Rid, skeleton);
                _skeleton = skeleton;
                for (int i = 0; i < _blendWeights.Length; i++)
                {
                    float weight = meshInstance.GetBlendShapeValue(i);
                    if (_blendWeights[i] != weight) RenderingServer.InstanceSetBlendShapeWeight(Rid, i, weight);
                    _blendWeights[i] = weight;
                }
            }
        }

        public void Hide()
        {
            if (_visible != false) RenderingServer.InstanceSetVisible(Rid, false);
            _visible = false;
        }

        public void Dispose()
        {
            if (_light != null) _light.Free();
            else RenderingServer.FreeRid(Rid);
            foreach (var name in _lightProperties) name.Dispose();
            foreach (var name in _shaderParameters) name.Dispose();
        }
    }
}
