using Lumora.Core.Assets;
using Lumora.Core.Components;
using Lumora.Core.Components.Gizmos;
using Lumora.Core.Components.Meshes;
using Lumora.Core.GodotUI;
using Lumora.Core.GodotUI.Inspectors;
using Lumora.Core.GodotUI.Wizards;
using Lumora.Core;

namespace Lumora.Source.Godot.Bootstrap;

/// <summary>
/// Centralized registration for all Godot-specific hooks.
/// </summary>
public static class GodotHookRegistry
{
    public static void RegisterAll()
    {
        // Core slot hook (MUST be registered first!)
        World.HookTypes.Register<Slot, Lumora.Godot.Hooks.SlotHook>();

        // Mesh hooks
        World.HookTypes.Register<ProceduralMesh, Lumora.Godot.Hooks.MeshHook>();
        World.HookTypes.Register<BoxMesh, Lumora.Godot.Hooks.MeshHook>();
        World.HookTypes.Register<QuadMesh, Lumora.Godot.Hooks.MeshHook>();
        World.HookTypes.Register<CylinderMesh, Lumora.Godot.Hooks.MeshHook>();
        World.HookTypes.Register<SphereMesh, Lumora.Godot.Hooks.MeshHook>();

        // Renderer hooks
        World.HookTypes.Register<MeshRenderer, Lumora.Godot.Hooks.MeshRendererHook>();
        World.HookTypes.Register<ModelData, Lumora.Godot.Hooks.ModelDataHook>();

        // Physics collider hooks
        World.HookTypes.Register<BoxCollider, Lumora.Godot.Hooks.PhysicsColliderHook>();
        World.HookTypes.Register<CapsuleCollider, Lumora.Godot.Hooks.PhysicsColliderHook>();
        World.HookTypes.Register<SphereCollider, Lumora.Godot.Hooks.PhysicsColliderHook>();
        World.HookTypes.Register<CylinderCollider, Lumora.Godot.Hooks.PhysicsColliderHook>();
        World.HookTypes.Register<RigidBody, Lumora.Godot.Hooks.RigidBodyHook>();
        World.HookTypes.Register<RespawnPlane, Lumora.Godot.Hooks.RespawnPlaneHook>();

        // Specialized hooks
        World.HookTypes.Register<Lumora.Core.Components.Avatar.LocalViewOverride, Lumora.Godot.Hooks.LocalViewOverrideHook>();
        World.HookTypes.Register<SkeletonBuilder, Lumora.Godot.Hooks.SkeletonHook>();
        World.HookTypes.Register<SkinnedMeshRenderer, Lumora.Godot.Hooks.SkinnedMeshHook>();
        World.HookTypes.Register<Lumora.Core.Components.HeadOutput, Lumora.Godot.Hooks.HeadOutputHook>();
        World.HookTypes.Register<CharacterController, Lumora.Godot.Hooks.CharacterControllerHook>();
        World.HookTypes.Register<Lumora.Core.Components.Avatar.GodotIKAvatar, Lumora.Godot.Hooks.GodotIKAvatarHook>();

        // Godot UI hooks
        World.HookTypes.Register<GodotUIPanel, Lumora.Godot.Hooks.GodotUI.GodotUIPanelHook>();
        World.HookTypes.Register<DashboardPanel, Lumora.Godot.Hooks.GodotUI.DashboardPanelHook>();
        World.HookTypes.Register<Lumora.Core.Components.UI.ContextMenuSystem, Lumora.Godot.Hooks.GodotUI.ContextMenuHook>();
        World.HookTypes.Register<GodotMaterialInspector, Lumora.Godot.Hooks.GodotUI.GodotMaterialInspectorHook>();
        World.HookTypes.Register<GodotMaterialColorPicker, Lumora.Godot.Hooks.GodotUI.GodotMaterialColorPickerHook>();
        World.HookTypes.Register<GodotImportDialogPanel, Lumora.Godot.Hooks.GodotUI.GodotImportDialogPanelHook>();
        World.HookTypes.Register<Nameplate, Lumora.Godot.Hooks.NameplateHook>();

        // Inspector hooks
        World.HookTypes.Register<SlotInspector, Lumora.Godot.Hooks.GodotUI.Inspectors.SlotInspectorHook>();
        World.HookTypes.Register<ComponentInspector, Lumora.Godot.Hooks.GodotUI.Inspectors.ComponentInspectorHook>();
        World.HookTypes.Register<SceneInspector, Lumora.Godot.Hooks.GodotUI.Inspectors.SceneInspectorHook>();
        World.HookTypes.Register<ComponentAttacher, Lumora.Godot.Hooks.GodotUI.Inspectors.ComponentAttacherHook>();

        // Gizmo hooks
        World.HookTypes.Register<SlotGizmo, Lumora.Godot.Hooks.Gizmos.SlotGizmoHook>();

        // Asset hooks
        AssetHookRegistry.Register<TextureAsset, Lumora.Godot.Hooks.TextureAssetHook>();
        AssetHookRegistry.Register<MaterialAsset, Lumora.Godot.Hooks.MaterialAssetHook>();
    }
}
