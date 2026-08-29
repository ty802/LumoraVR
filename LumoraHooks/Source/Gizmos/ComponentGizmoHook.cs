// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;
using Lumora.Core.Components.Gizmos;
using Lumora.Godot.Helpers;

namespace Lumora.Godot.Hooks.Gizmos;

#nullable enable

// Draws every component gizmo's wireframe: one line-primitive surface per gizmo, fed straight from
// the segment list the engine built.
//
// Bound to the ABSTRACT base, so all fifteen gizmo types share this one hook - the hook registry
// resolves through base types, and there is nothing type-specific to do here anyway. The engine side
// decides what the shape IS; this only puts it on screen.
//
// Draws, never measures. The segments arrive in the shape slot's own space and that slot already
// carries the target's transform, so following a moving object reaches no code here at all, and
// geometry is re-emitted only when the engine bumps its wire version. Vertex colours come with the
// buffer, which is what lets one surface carry a LOD group's four differently-shaded rings and a
// plunger's two threshold ticks without a material per colour. -xlinka
[ImplementableHook(typeof(ComponentGizmo))]
public sealed class ComponentGizmoHook : ComponentHook<ComponentGizmo>
{
	private StandardMaterial3D? _material;
	private MeshInstance3D? _instance;
	private ImmediateMesh? _mesh;
	private Node3D? _parent;
	private int _drawnVersion = -1;

	public override void Initialize()
	{
		base.Initialize();

		_material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			VertexColorUseAsAlbedo = true,
			// Editor chrome has to be findable through the object it annotates: a collider wireframe
			// that is occluded by the mesh it wraps is no use at all.
			NoDepthTest = true,
			AlbedoColor = new Color(1f, 1f, 1f, 1f),
		};
		UpdateVisuals();
	}

	public override void ApplyChanges()
	{
		UpdateVisuals();
	}

	private void UpdateVisuals()
	{
		EnsureMesh();
		if (_instance == null || !GodotObject.IsInstanceValid(_instance))
			return;

		if (_drawnVersion != Owner.WireVersion)
		{
			_drawnVersion = Owner.WireVersion;
			DrawWire();
		}
		_instance.Visible = Owner.IsVisible;
	}

	// The shape slot is engine content created with the gizmo, so its node is just the parent of this
	// one. Re-parent if the slot was rebuilt under us.
	private void EnsureMesh()
	{
		var parent = Owner.ShapeRoot?.GetGeneratedNode3D(forceGenerate: true);
		if (parent == null || !GodotObject.IsInstanceValid(parent))
			return;

		if (_instance != null && GodotObject.IsInstanceValid(_instance) && ReferenceEquals(_parent, parent))
			return;

		if (_instance != null && GodotObject.IsInstanceValid(_instance))
			_instance.QueueFree();

		_mesh ??= new ImmediateMesh();
		_instance = new MeshInstance3D
		{
			Name = "GizmoWire",
			Mesh = _mesh,
			MaterialOverride = _material,
			// Chrome is not part of the scene it annotates: it must not cast, receive or occlude.
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		_parent = parent;
		parent.AddChild(_instance);
		_drawnVersion = -1;
	}

	private void DrawWire()
	{
		if (_mesh == null || _material == null)
			return;

		_mesh.ClearSurfaces();

		var vertices = Owner.WireVertices;
		var colors = Owner.WireColors;
		// Odd counts cannot happen (the builder only ever appends pairs), but a surface begun and never
		// closed leaves the mesh in a broken state, so the loop is written to be safe either way.
		int count = vertices.Count & ~1;
		if (count == 0)
			return;

		_mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _material);
		for (int i = 0; i < count; i++)
		{
			var c = colors.Count > i ? colors[i] : default;
			_mesh.SurfaceSetColor(new Color(c.r, c.g, c.b, c.a));
			var v = vertices[i];
			_mesh.SurfaceAddVertex(new Vector3(v.x, v.y, v.z));
		}
		_mesh.SurfaceEnd();
	}

	public override void Destroy(bool destroyingWorld)
	{
		if (!destroyingWorld)
		{
			if (_instance != null && GodotObject.IsInstanceValid(_instance))
				_instance.QueueFree();
			_material?.Dispose();
			_mesh?.Dispose();
		}

		_instance = null;
		_parent = null;
		_material = null;
		_mesh = null;

		base.Destroy(destroyingWorld);
	}
}
