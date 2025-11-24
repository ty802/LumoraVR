using Godot;
using Lumora.Core.Components;
using Lumora.Core.Math;
using System.Collections.Generic;
using AquaLogger = Lumora.Core.Logging.Logger;

namespace Aquamarine.Godot.Hooks;

/// <summary>
/// Hook for MeshRenderer component → Godot MeshInstance3D.
/// Platform mesh renderer hook for Godot.
/// </summary>
public class MeshRendererHook : ComponentHook<MeshRenderer>
{
	private MeshInstance3D _meshInstance;
	private Dictionary<int, Material> _materials = new Dictionary<int, Material>();
	
	public override void Initialize()
	{
		base.Initialize();
		
		// Create MeshInstance3D for rendering
		_meshInstance = new MeshInstance3D();
		_meshInstance.Name = "MeshRenderer";
		attachedNode.AddChild(_meshInstance);
		
		// Apply initial mesh if available
		ApplyMesh();
		
		AquaLogger.Log($"MeshRendererHook: Initialized for slot '{Owner.Slot.SlotName.Value}'");
	}
	
	public override void ApplyChanges()
	{
		if (_meshInstance == null || !GodotObject.IsInstanceValid(_meshInstance))
			return;
		
		// Check if mesh changed
		if (Owner.Mesh.GetWasChangedAndClear())
		{
			ApplyMesh();
		}
		
		// Update enabled state
		bool enabled = Owner.Enabled;
		if (_meshInstance.Visible != enabled)
		{
			_meshInstance.Visible = enabled;
		}
		
		// TODO: Handle materials when material system is implemented
		// if (Owner.MaterialsChanged)
		// {
		//     ApplyMaterials();
		// }
	}
	
	private void ApplyMesh()
	{
		if (Owner.Mesh == null)
		{
			_meshInstance.Mesh = null;
			return;
		}
		
		// Create a simple sphere mesh for now (avatar body parts)
		// TODO: Properly convert ProceduralMesh to Godot Mesh when mesh system is complete
		var sphereMesh = new SphereMesh();
		sphereMesh.Height = 0.1f;
		sphereMesh.RadialSegments = 16;
		sphereMesh.Rings = 8;
		
		// Apply appropriate size based on slot name (for avatar parts)
		string slotName = Owner.Slot.SlotName.Value;
		
		// Head is bigger
		if (slotName.Contains("Head"))
		{
			sphereMesh.Height = 0.15f;
			sphereMesh.RadialSegments = 32;
			sphereMesh.Rings = 16;
		}
		// Hands and feet are smaller
		else if (slotName.Contains("Hand") || slotName.Contains("Foot"))
		{
			sphereMesh.Height = 0.08f;
		}
		// Joints (elbow, knee, shoulder, hip) are medium
		else if (slotName.Contains("Elbow") || slotName.Contains("Knee") || 
		         slotName.Contains("Shoulder") || slotName.Contains("Hip"))
		{
			sphereMesh.Height = 0.1f;
		}
		// Body parts (chest, spine, pelvis) are larger
		else if (slotName.Contains("Chest") || slotName.Contains("Spine") || 
		         slotName.Contains("Pelvis") || slotName.Contains("Neck"))
		{
			sphereMesh.Height = 0.12f;
		}
		
		_meshInstance.Mesh = sphereMesh;
		
		// Create a simple material for visibility
		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(0.8f, 0.8f, 0.8f); // Light gray
		material.MetallicSpecular = 0.2f;
		material.Roughness = 0.8f;
		
		// Set material
		_meshInstance.SetSurfaceOverrideMaterial(0, material);
		
		AquaLogger.Log($"MeshRendererHook: Applied mesh for '{slotName}'");
	}
	
	public override void Destroy(bool destroyingWorld)
	{
		if (!destroyingWorld && _meshInstance != null && GodotObject.IsInstanceValid(_meshInstance))
		{
			_meshInstance.QueueFree();
		}
		
		_meshInstance = null;
		_materials.Clear();
		
		base.Destroy(destroyingWorld);
	}
}
